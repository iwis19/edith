using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Edith.Ask;
using Edith.Capture;
using Edith.Grab;
using Edith.Pinch;
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
    /// leaves any way but the pocket stops its request, and a pocketed one lets it finish.
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
        /// Writes the cards' text.
        [SerializeField] Font font;

        const string CameraNotReady = "The camera is not ready.";
        const float AssumedSubjectMeters = 1.5f;
        const float MinSubjectMeters = 0.25f;
        const float MaxSubjectMeters = 6f;
        // Where in the box to measure, as fractions of its width and height. The centre wins when it hits, because
        // the object is usually there; otherwise the median of the rest, so one stray hit cannot drag the crop.
        static readonly Vector2[] DepthSamples =
            { new Vector2(0.5f, 0.5f), new Vector2(0.35f, 0.35f), new Vector2(0.65f, 0.35f), new Vector2(0.35f, 0.65f), new Vector2(0.65f, 0.65f) };

        sealed class Release
        {
            public int LookId;
            public SightBox Box;
            public Task<CameraFrame> Frame;
            /// Null until the photo is ready, and for good when the camera failed.
            public LookClient Request;
            public bool Confirmed;
        }

        readonly List<Release> releases = new List<Release>();
        readonly List<(Snapshot snapshot, LookClient request)> live = new List<(Snapshot, LookClient)>();
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
            ask.Bind(room);
        }

        void OnEnable() => pinch.Stepped += OnPinch;
        void OnDisable()
        {
            pinch.Stepped -= OnPinch;
            foreach (var release in releases) release.Request?.Cancel();
            foreach (var (_, request) in live) request.Cancel();
        }

        void OnPinch(PinchReading reading)
        {
            switch (reading.Event)
            {
                case PinchEvent.Tap:
                    Clear(reading.Start);
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
                    release.Request = LookClient.Send(release.LookId, release.Frame.Result.Jpeg, release.Frame.Result.CapturedAt);
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
            for (var i = live.Count - 1; i >= 0; i--)
            {
                var (snapshot, request) = live[i];
                if (snapshot == null || snapshot.Leaving)
                {
                    request.Cancel();
                    live.RemoveAt(i);
                    continue;
                }
                while (request.TryNext(out var e)) snapshot.Apply(e);
                if (snapshot.Card.Finished) live.RemoveAt(i);
            }
        }

        void OnPocketed(Snapshot snapshot) => live.RemoveAll(entry => entry.snapshot == snapshot);

        void Clear(Vector3 pinchPoint)
        {
            var hit = room.Hit(eye.position, pinchPoint);
            if (hit == null)
            {
                Debug.Log("[EDITH look] tap ignored, it did not land on a snapshot");
                return;
            }
            room.Dismiss(hit);
            Debug.Log($"[EDITH look] snapshot {hit.Id} cleared by a tap on it");
        }

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
            if (release.Request != null) live.Add((snapshot, release.Request));
            else snapshot.Apply(LookEvent.Failure("camera", CameraNotReady));
            Debug.Log($"[EDITH look] snapshot {snapshot.Id} hung for look {release.LookId}{(photo != null ? $" with a {photo.width}x{photo.height} photo" : " without a photo")}");
        }

        Task<CameraFrame> Capture(in SightBox box)
        {
            try
            {
                var depth = SubjectDepth(box);
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
