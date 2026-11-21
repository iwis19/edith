using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Edith.Ask;
using Edith.Backend;
using Edith.Capture;
using Edith.Grab;
using Edith.Pinch;
using Edith.Scan;
using Edith.Snapshots;
using Meta.XR;
using UnityEngine;

namespace Edith.Look
{
    /// Turns a pinch-dragged box into a snapshot of what the camera sees through it. The photo is read back the
    /// moment the pinch lets go and goes to the backend as soon as it is ready; the snapshot hangs once the release
    /// stands, since a pinch that resumes within the rejoin window is the same drag and nothing of it should flash up.
    /// The backend's answer writes the snapshot's card. While a box is dragged, the snapshots it covers fade out. A
    /// quick pinch on a snapshot clears it, and only a pinch that lands on the photo itself does. A snapshot that
    /// leaves any way but the pocket stops its request, and a pocketed one lets it finish and saves its photo as a
    /// memory.
    public sealed class LookController : MonoBehaviour
    {
        [SerializeField] PinchInput pinch;
        [SerializeField] HeadsetCamera headsetCamera;
        [SerializeField] SnapshotGrab grab;
        [SerializeField] AskController ask;
        [SerializeField] Transform eye;
        /// Measures how far away the framed object is, so the crop lines up with it. Optional: without it, or when
        /// depth has no answer, the crop assumes the object is AssumedSubjectMeters away.
        [SerializeField] EnvironmentRaycastManager environmentRaycast;
        /// Draws the photos so real objects in front of them hide them.
        [SerializeField] Material occludedMaterial;
        [SerializeField] Font font;
        /// Opens a scan pin as a Look when a quick pinch lands on it rather than on a snapshot. Optional.
        [SerializeField] ScanController scan;

        const string CameraNotReady = "The camera is not ready.";
        // Often enough that the shared HTTPS connection is still open when the next Look goes out.
        const float KeepWarmSeconds = 45f;
        const float AssumedSubjectMeters = 1.5f;
        const float MinSubjectMeters = 0.25f;
        const float MaxSubjectMeters = 6f;
        // Where in the box to measure, as fractions of its width and height. The centre wins when it hits, because
        // the object is usually there; otherwise the median of the rest, so one stray hit cannot drag the crop.
        static readonly Vector2[] DepthSamples =
            { new Vector2(0.5f, 0.5f), new Vector2(0.35f, 0.35f), new Vector2(0.65f, 0.35f), new Vector2(0.35f, 0.65f), new Vector2(0.65f, 0.65f) };

        /// A snapshot on the wall with the photo it was made from. The request is null when the camera failed.
        sealed class Hung
        {
            public Snapshot Snapshot;
            public LookClient Request;
            public byte[] Jpeg;
        }

        sealed class Release
        {
            public int LookId;
            public LookSource Source;
            public SightBox Box;
            public Task<CameraFrame> Frame;
            /// Null until the photo is ready, and for good when the camera failed.
            public LookClient Request;
            public bool Confirmed;
            /// A scan pin's hook, which its card says instead of the backend's fact. Null for a drawn box.
            public string Caption;
        }

        readonly List<Release> releases = new List<Release>();
        readonly List<Hung> hung = new List<Hung>();
        readonly List<float> sampledDepths = new List<float>(DepthSamples.Length);
        SnapshotRoom room;
        int nextLookId;

        /// The snapshots in the room.
        public SnapshotRoom Room => room;

        void Awake()
        {
            room = new SnapshotRoom(transform, occludedMaterial, font);
            room.Pocketed += OnPocketed;
            grab.Bind(room);
            ask.Bind(room, headsetCamera);
        }

        IEnumerator Start()
        {
            var settings = BackendSettings.Load();
            if (settings == null) yield break;
            var every = new WaitForSecondsRealtime(KeepWarmSeconds);
            while (true)
            {
                LookClient.Warm(settings.Url);
                yield return every;
            }
        }

        void OnEnable()
        {
            pinch.Stepped += OnPinch;
            if (scan != null) scan.Opened += OpenBox;
        }

        void OnDisable()
        {
            pinch.Stepped -= OnPinch;
            if (scan != null) scan.Opened -= OpenBox;
            foreach (var release in releases) release.Request?.Cancel();
            foreach (var entry in hung) entry.Request?.Cancel();
        }

        void OnPinch(PinchReading reading)
        {
            switch (reading.Event)
            {
                case PinchEvent.Tap:
                    // Photos hang at hand depth, in front of any pin, so a pin opens only when the tap met no photo.
                    if (!Clear(reading.Start) && !Open(reading.Start))
                        Debug.Log("[EDITH look] tap ignored, it landed on no snapshot or pin");
                    break;
                case PinchEvent.Cancelled:
                    room.CancelPreview();
                    break;
                case PinchEvent.Completed:
                    releases.Add(new Release { LookId = nextLookId++, Box = reading.Box, Frame = Capture(reading.Box) });
                    break;
                case PinchEvent.Resumed:
                    // The release was a tracking dropout: the drag goes on and the photo is not wanted.
                    foreach (var release in releases)
                        if (!release.Confirmed) release.Request?.Cancel();
                    releases.RemoveAll(release => !release.Confirmed);
                    break;
                case PinchEvent.Confirmed:
                    foreach (var release in releases) release.Confirmed = true;
                    break;
            }
            if (reading.Phase == PinchPhase.Dragging && reading.HasBox) room.Preview(reading.Box);
        }

        void Update()
        {
            foreach (var release in releases)
                if (release.Request == null && release.Frame.IsCompletedSuccessfully)
                    release.Request = LookClient.Send(release.LookId, release.Source, release.Frame.Result.Jpeg, release.Frame.Result.CapturedAt);
            while (releases.Count > 0 && releases[0].Confirmed && releases[0].Frame.IsCompleted)
            {
                Hang(releases[0]);
                releases.RemoveAt(0);
            }
            Drain();
        }

        /// Writes what the backend has sent into each snapshot's card. A snapshot that left stops its request.
        void Drain()
        {
            for (var i = hung.Count - 1; i >= 0; i--)
            {
                var entry = hung[i];
                if (entry.Snapshot == null || entry.Snapshot.Leaving)
                {
                    entry.Request?.Cancel();
                    hung.RemoveAt(i);
                    continue;
                }
                if (entry.Request == null) continue;
                while (entry.Request.TryNext(out var e)) entry.Snapshot.Apply(e);
            }
        }

        void OnPocketed(Snapshot snapshot)
        {
            var index = hung.FindIndex(entry => entry.Snapshot == snapshot);
            if (index < 0) return;
            var entry = hung[index];
            hung.RemoveAt(index);
            var skip = CollectClient.SkipReason(snapshot.Card);
            if (skip != null) Debug.Log($"[EDITH collect] snapshot {snapshot.Id} not collected, {skip}");
            else CollectClient.Send(snapshot.Card.LookRef, entry.Jpeg);
        }

        bool Clear(Vector3 pinchPoint)
        {
            var hit = room.Hit(eye.position, pinchPoint);
            if (hit == null) return false;
            room.Dismiss(hit);
            Debug.Log($"[EDITH look] snapshot {hit.Id} cleared by a tap on it");
            return true;
        }

        /// Takes a Look of the scan pin the pinch is in line with, as if its box had been drawn and let go.
        bool Open(Vector3 pinchPoint)
        {
            if (scan == null || !scan.TryOpen(pinchPoint, new Pose(eye.position, eye.rotation), out var box, out var pin)) return false;
            OpenBox(box, pin);
            return true;
        }

        // The pin knows how far its object is, so the photo is cropped there rather than at whatever depth meets
        // first through the box, such as the window in front of a view.
        void OpenBox(SightBox box, ScanPin pin) =>
            releases.Add(new Release
            {
                LookId = nextLookId++, Source = LookSource.Scan, Box = box, Confirmed = true, Caption = pin.Hook,
                Frame = Capture(box, Vector3.Dot(pin.Point - box.Eye.position, box.Eye.rotation * Vector3.forward)),
            });

        void Hang(Release release)
        {
            Texture2D photo = null;
            if (release.Frame.IsFaulted)
            {
                Debug.Log($"[EDITH look] no photo: {release.Frame.Exception.GetBaseException().Message}");
            }
            else
            {
                photo = new Texture2D(2, 2, TextureFormat.RGB24, false);
                photo.LoadImage(release.Frame.Result.Jpeg);
            }
            var snapshot = room.Place(release.Box, photo);
            if (release.Caption != null) snapshot.Caption = release.Caption;
            hung.Add(new Hung { Snapshot = snapshot, Request = release.Request, Jpeg = release.Frame.IsFaulted ? null : release.Frame.Result.Jpeg });
            if (release.Request == null) snapshot.Apply(LookEvent.Failure("camera", CameraNotReady));
            Debug.Log($"[EDITH look] snapshot {snapshot.Id} hung for look {release.LookId}{(photo != null ? $" with a {photo.width}x{photo.height} photo" : " without a photo")}");
        }

        /// `subjectDepth`, when known, is how far the framed object is along the box's forward axis.
        Task<CameraFrame> Capture(in SightBox box, float? subjectDepth = null)
        {
            try
            {
                var depth = subjectDepth ?? SubjectDepth(box);
                var t = box.Tangent;
                return headsetCamera.CaptureAsync(new WorldQuad(
                    box.PointAt(t.xMin, t.yMin, depth), box.PointAt(t.xMax, t.yMin, depth),
                    box.PointAt(t.xMax, t.yMax, depth), box.PointAt(t.xMin, t.yMax, depth)));
            }
            catch (Exception e)
            {
                return Task.FromException<CameraFrame>(e);
            }
        }

        float SubjectDepth(in SightBox box)
        {
            if (environmentRaycast == null) return AssumedSubjectMeters;
            var toBox = Quaternion.Inverse(box.Eye.rotation);
            sampledDepths.Clear();
            float? centre = null;
            for (var i = 0; i < DepthSamples.Length; i++)
            {
                var t = box.Tangent;
                var towards = box.PointAt(Mathf.Lerp(t.xMin, t.xMax, DepthSamples[i].x), Mathf.Lerp(t.yMin, t.yMax, DepthSamples[i].y), 1f) - box.Eye.position;
                if (!environmentRaycast.Raycast(new Ray(box.Eye.position, towards), out var hit, MaxSubjectMeters)) continue;
                var depth = (toBox * (hit.point - box.Eye.position)).z;
                if (i == 0) centre = depth;
                else sampledDepths.Add(depth);
            }
            if (centre == null && sampledDepths.Count == 0) return AssumedSubjectMeters;
            sampledDepths.Sort();
            return Mathf.Clamp(centre ?? sampledDepths[sampledDepths.Count / 2], MinSubjectMeters, MaxSubjectMeters);
        }
    }
}
