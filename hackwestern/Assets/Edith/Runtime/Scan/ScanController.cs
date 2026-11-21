using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Edith.Backend;
using Edith.Capture;
using Edith.Hands;
using Edith.Snapshots;
using Meta.XR;
using UnityEngine;

namespace Edith.Scan
{
    /// A pin in the room: where it sits, what it is on, and its hook.
    public readonly struct ScanPin
    {
        public readonly Vector3 Point;
        public readonly string Name, Hook;
        /// Beyond depth's reach: the pin only points the way.
        public readonly bool Far;

        public ScanPin(Vector3 point, string name, string hook, bool far)
        {
            Point = point;
            Name = name;
            Hook = hook;
            Far = far;
        }
    }

    /// Scan, end to end on the headset. Both palms held open and facing away send one whole camera frame to the
    /// backend, which picks the few most interesting things in it. Each gets a pin: a dot on the object, placed by
    /// depth, with a short hook above it. A new scan replaces the pins of the last, and recentring the view clears
    /// them. TryOpen turns a pinch on a pin into the box a Look needs.
    public sealed class ScanController : MonoBehaviour
    {
        [SerializeField] LeftHandSource leftHand;
        [SerializeField] RightHandSource rightHand;
        [SerializeField] HeadsetCamera headsetCamera;
        [SerializeField] PassthroughCameraAccess cameraAccess;
        /// Optional: without it every pin sits at tuning.assumedPinMeters.
        [SerializeField] EnvironmentRaycastManager environmentRaycast;
        [SerializeField] Transform eye;
        [SerializeField] Font font;
        [SerializeField] Material material;
        [SerializeField] ScanTuning tuning = new ScanTuning();

        // A pin sits this far in front of the surface it was measured on, so the surface does not occlude it.
        const float LiftMeters = 0.04f;
        // The backend gives up at 7 s and says why; past this the person is told EDITH could not be reached.
        const float ReplySeconds = 8f;
        const float NearLogSeconds = 1f;
        // The depth remembered for a scan: cells about 4 degrees across, a few sight lines measured each frame so
        // the whole frame is covered several times a second.
        const int DepthColumns = 16, DepthRows = 12, DepthRaysPerFrame = 24;
        // Where the ring and the status line sit for a scan that did not start from the pose.
        const float StatusAhead = 0.45f, StatusBelow = 0.1f;

        sealed class Placed
        {
            public WorldQuad Quad;
            public ScanMarks.Mark Mark;
        }

        readonly List<ScanPin> pins = new List<ScanPin>();
        readonly List<Placed> placed = new List<Placed>();
        readonly List<Vector3> points = new List<Vector3>();
        ScanPoseTracker tracker;
        ScanMarks marks;
        ScanClient client;
        AudioSource sound;
        Vector3 ringAt;
        float holdProgress;
        bool scanning;
        int scanCount;
        // Bumped on every recentre: a reply that arrives after one was measured in the old tracking space.
        int recentres;
        bool listeningForRecentre;
        float nearLoggedAt = float.NegativeInfinity;
        // Set between a scan's capture and its reply. The reply takes seconds, and the head is free to move meanwhile.
        ScanDepthGrid depth;
        Pose depthPose;
        int depthCursor;

        /// The pins in the room, most interesting first.
        public IReadOnlyList<ScanPin> Pins => pins;

        /// What the last scan ended with: how many pins landed, or why none did. Null before the first scan.
        public string LastResult { get; private set; }

        void Awake()
        {
            tracker = new ScanPoseTracker(tuning);
            marks = new ScanMarks(font, material);
            sound = gameObject.AddComponent<AudioSource>();
            sound.spatialBlend = 0f;
            sound.playOnAwake = false;
        }

        void Start()
        {
            var settings = BackendSettings.Load();
            if (settings != null) client = new ScanClient(settings.Url, settings.DeviceToken, BackendSettings.ProfileId);
        }

        void OnDisable()
        {
            if (listeningForRecentre && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentred;
            listeningForRecentre = false;
        }

        void OnDestroy() => marks.Dispose();

        void Update()
        {
            // OVRManager makes its display after the scene's first frames, so the subscription waits for it.
            if (!listeningForRecentre && OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose += OnRecentred;
                listeningForRecentre = true;
            }

            for (var i = pins.Count - 1; i >= 0; i--)
            {
                if (!pins[i].Far || (pins[i].Point - eye.position).magnitude >= tuning.farFadeMeters) continue;
                Debug.Log($"[EDITH scan] far pin \"{pins[i].Name}\" faded, walked within {tuning.farFadeMeters:F0} m of it");
                RemovePin(i);
            }

            if (depth != null) RememberDepth();
            marks.SetRing(ringAt, holdProgress, scanning);
            marks.Tick(eye.position);
        }

        // After hand tracking has updated, like the pinch and the grab.
        void LateUpdate()
        {
            var pose = tracker.Step(leftHand.Read(), rightHand.Read());
            holdProgress = pose.Progress;
            if (pose.Progress > 0f) ringAt = pose.Between;
            if (pose.Fired)
            {
                Debug.Log($"[EDITH scan] pose fired: left {pose.Left}, right {pose.Right}");
                Scan();
            }
            // A near miss logs what each hand read, so the thresholds can be tuned from real hands.
            else if (pose.Near && pose.Progress == 0f && !scanning && Time.time - nearLoggedAt >= NearLogSeconds)
            {
                nearLoggedAt = Time.time;
                Debug.Log($"[EDITH scan] near: left {pose.Left}, right {pose.Right}");
            }
        }

        /// Starts a scan of what the camera sees now. The palms-out pose calls this; so can any other trigger.
        /// Ignored while a scan is in flight.
        public async void Scan()
        {
            if (scanning)
            {
                Debug.Log("[EDITH scan] ignored, a scan is still in flight");
                return;
            }
            if (holdProgress == 0f) ringAt = eye.position + eye.forward * StatusAhead - eye.up * StatusBelow;
            marks.HideStatus();
            if (client == null || headsetCamera.Status != CameraStatus.Streaming)
            {
                Debug.Log(client == null ? "[EDITH scan] no backend in this build" : $"[EDITH scan] camera is {headsetCamera.Status}");
                Fail(client == null ? "Couldn't reach EDITH." : "The camera isn't ready.");
                return;
            }

            scanning = true;
            var id = ++scanCount;
            var space = recentres;
            var started = Time.realtimeSinceStartup;
            int Ms() => Mathf.RoundToInt((Time.realtimeSinceStartup - started) * 1000f);
            Play(659.3f, 987.8f);
            try
            {
                var capturedAt = DateTime.UtcNow;
                var frame = await headsetCamera.CaptureFullAsync();
                if (this == null) return;
                Debug.Log($"[EDITH scan] {id} captured {frame.Size.x}x{frame.Size.y}, {frame.Jpeg.Length / 1024} KB, {Ms()} ms in");
                if (environmentRaycast != null)
                {
                    depth = new ScanDepthGrid(DepthColumns, DepthRows);
                    depthPose = frame.CameraPose;
                    depthCursor = 0;
                }
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(ReplySeconds));
                var picks = await client.ScanAsync(frame.Jpeg, capturedAt, cancel.Token);
                if (this == null) return;
                Debug.Log($"[EDITH scan] {id} reply {Ms()} ms in, {picks.Count} picks");
                if (space != recentres)
                {
                    Debug.Log($"[EDITH scan] {id} dropped, the view was recentred while it was read");
                    return;
                }
                Place(id, frame.CameraPose, picks);
                if (pins.Count > 0)
                {
                    LastResult = $"{id}: {pins.Count} pins";
                    Play(1318.5f);
                }
                else Fail("Nothing stands out here.");
            }
            catch (Exception e)
            {
                if (this == null) return;
                var why = e is OperationCanceledException ? "timeout" : e.GetBaseException().Message;
                Debug.Log($"[EDITH scan] {id} failed {Ms()} ms in: {why}");
                // No answer at all is nearly always the headset's Wi-Fi, such as a network waiting on a sign-in page.
                var unreachable = e is OperationCanceledException || e is HttpRequestException;
                Fail(unreachable ? "Couldn't reach EDITH. Check the Wi-Fi." : "Couldn't look around.");
            }
            finally
            {
                scanning = false;
                depth = null;
            }
        }

        /// Turns a pinch at `pinchPoint`, seen from `eye`, into a Look of the pin it is in line with. `box` is the
        /// box to hang and photograph, as if the person had drawn it around the pin's object, and the pin leaves the
        /// room. False when no pin is in line with the pinch, or the pin's object is too close to frame.
        public bool TryOpen(Vector3 pinchPoint, Pose eye, out SightBox box)
        {
            box = default;
            if (pinchPoint == eye.position) return false;
            points.Clear();
            foreach (var pin in pins) points.Add(pin.Point);
            var index = ScanGeometry.NearestToLine(new Ray(eye.position, pinchPoint - eye.position), points, tuning.pinTapDegrees);
            if (index < 0) return false;

            var distance = (pins[index].Point - eye.position).magnitude;
            var cardDepth = Mathf.Min(tuning.cardDepthMeters, tuning.cardDepthShare * distance);
            if (ScanGeometry.BoxAround(eye.position, placed[index].Quad, cardDepth) is not { } around)
            {
                Debug.Log($"[EDITH scan] pin \"{pins[index].Name}\" not opened, too close to frame from {distance:F2} m");
                return false;
            }
            Debug.Log($"[EDITH scan] open \"{pins[index].Name}\" {distance:F2} m away, card at {cardDepth:F2} m");
            RemovePin(index);
            box = around;
            return true;
        }

        /// The pin at the smallest angle from the line of sight of `eye`, within `maxDegrees` of it. `degrees` is
        /// that angle, to compare with other things in view. False when there is none.
        public bool TryNearest(Pose eye, float maxDegrees, out ScanPin pin, out float degrees)
        {
            pin = default;
            degrees = 0f;
            points.Clear();
            foreach (var candidate in pins) points.Add(candidate.Point);
            var gaze = new Ray(eye.position, eye.rotation * Vector3.forward);
            var index = ScanGeometry.NearestToLine(gaze, points, maxDegrees);
            if (index < 0) return false;
            pin = pins[index];
            degrees = Vector3.Angle(gaze.direction, pin.Point - eye.position);
            return true;
        }

        // Recentring moves the whole tracking space, so pins placed before it would float in the wrong spot.
        void OnRecentred()
        {
            recentres++;
            Debug.Log($"[EDITH scan] recentred, {pins.Count} pins cleared");
            ClearPins();
        }

        // Measures a few of the captured frame's sight lines against the depth the headset has now. Lines the head
        // has turned away from, or that meet a hand, give nothing and are tried again on a later pass.
        void RememberDepth()
        {
            var facing = depthPose.rotation * Vector3.forward;
            for (var i = 0; i < DepthRaysPerFrame; i++)
            {
                var cell = depthCursor;
                depthCursor = (depthCursor + 1) % depth.Count;
                var ray = cameraAccess.ViewportPointToRay(depth.Viewport(cell), depthPose);
                if (!environmentRaycast.Raycast(ray, out var hit, tuning.maxPinMeters)) continue;
                var along = hit.point - ray.origin;
                if (along.magnitude >= tuning.minPinMeters) depth.Record(cell, Vector3.Dot(along, facing));
            }
        }

        void Place(int id, Pose camera, List<ScanPick> picks)
        {
            ClearPins();
            var facing = camera.rotation * Vector3.forward;
            Ray Through(float x, float y) => cameraAccess.ViewportPointToRay(new Vector2(x, y), camera);
            if (depth != null) Debug.Log($"[EDITH scan] {id} depth remembered for {depth.Known} of {depth.Count} cells");
            foreach (var pick in picks)
            {
                var ray = Through(pick.ViewportPoint.x, pick.ViewportPoint.y);
                var answer = Probe(ray, out var hitMeters);
                var (meters, source) = ScanGeometry.PinDistance(
                    answer, hitMeters, depth?.At(pick.ViewportPoint), Vector3.Dot(ray.direction, facing), tuning);
                var onObject = ray.GetPoint(meters);
                var box = pick.ViewportBox;
                var quad = ScanGeometry.QuadOnPlane(Through(box.xMin, box.yMin), Through(box.xMax, box.yMin),
                    Through(box.xMax, box.yMax), Through(box.xMin, box.yMax), onObject, facing);
                if (quad == null)
                {
                    Debug.Log($"[EDITH scan] {id} pin \"{pick.Name}\" skipped, its box has no place in the room");
                    continue;
                }

                var far = source == PinSource.Far;
                var pin = new ScanPin(onObject - ray.direction * LiftMeters, pick.Name, pick.Hook, far);
                pins.Add(pin);
                placed.Add(new Placed { Quad = quad.Value, Mark = marks.AddPin(pin.Point, far ? $"{pick.Hook} (far)" : pick.Hook) });
                Debug.Log($"[EDITH scan] {id} pin \"{pick.Name}\" {meters:F2} m away ({source}, depth {answer}), hook \"{pick.Hook}\"");
            }
        }

        DepthAnswer Probe(Ray ray, out float meters)
        {
            meters = 0f;
            if (environmentRaycast == null) return DepthAnswer.None;
            if (environmentRaycast.Raycast(ray, out var hit, tuning.maxPinMeters))
            {
                meters = (hit.point - ray.origin).magnitude;
                return DepthAnswer.Hit;
            }
            return hit.status == EnvironmentRaycastHitStatus.NoHit ? DepthAnswer.Nothing : DepthAnswer.None;
        }

        void ClearPins()
        {
            for (var i = pins.Count - 1; i >= 0; i--) RemovePin(i);
        }

        void RemovePin(int index)
        {
            marks.Remove(placed[index].Mark);
            placed.RemoveAt(index);
            pins.RemoveAt(index);
        }

        /// A scan that ends with no pins says why where the ring was, with a low tone.
        void Fail(string message)
        {
            LastResult = message;
            marks.ShowStatus(ringAt, message);
            Play(440f);
        }

        void Play(float first, float second = 0f)
        {
            // Each tone is made fresh: a clip made earlier plays nothing once the sound output has been reopened.
            var clip = Tone(first, second);
            sound.PlayOneShot(clip);
            Destroy(clip, clip.length + 0.5f);
        }

        /// A short soft tone, or a rising two-note one when given two pitches.
        static AudioClip Tone(float first, float second)
        {
            const int rate = 48000;
            var samples = new float[rate * (second > 0f ? 26 : 18) / 100];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var late = second > 0f && t >= 0.1f;
                var local = late ? t - 0.1f : t;
                var envelope = Mathf.Clamp01(local * 200f) * Mathf.Exp(-local * 16f);
                samples[i] = 0.35f * envelope * Mathf.Sin(2f * Mathf.PI * (late ? second : first) * t);
            }
            var clip = AudioClip.Create("EDITH scan tone", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
