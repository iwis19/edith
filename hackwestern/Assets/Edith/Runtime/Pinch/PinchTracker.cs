using System.Collections.Generic;
using Edith.Hands;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Pinch
{
    public enum PinchPhase { Idle, Pressed, Dragging, Released }

    /// Non-None on exactly one frame per outcome. Completed is followed by exactly one of Confirmed (the release
    /// stands) or Resumed (it was a tracking dropout and the same drag goes on).
    public enum PinchEvent { None, Tap, Completed, Confirmed, Resumed, Cancelled }

    public enum PinchRejection { None, PalmFacingUser, TooClose, TooSmall, TooNarrow, Lost }

    public readonly struct PinchReading
    {
        public readonly PinchPhase Phase;
        public readonly PinchEvent Event;
        public readonly PinchRejection Rejection;
        /// The live box while Dragging; the released box while Released and on Completed and Confirmed.
        public readonly bool HasBox;
        public readonly SightBox Box;
        /// Where the pinch started. On a Tap, where the tap landed.
        public readonly Vector3 Start;

        public PinchReading(PinchPhase phase, PinchEvent pinchEvent, PinchRejection rejection, bool hasBox, SightBox box, Vector3 start)
        {
            Phase = phase;
            Event = pinchEvent;
            Rejection = rejection;
            HasBox = hasBox;
            Box = box;
            Start = start;
        }
    }

    /// Right-hand pinch, drag and release. Deterministic in HandFrame.Time.
    public sealed class PinchTracker
    {
        struct Sample
        {
            public float Time;
            public Vector3 Point;
            public Pose Eye;
        }

        const float KeepSeconds = 0.5f;

        readonly PinchTuning tuning;
        readonly List<Sample> recent = new List<Sample>();
        PinchPhase phase;
        Vector3 start;
        float lastSeen;
        float unpinchedSince = -1f;
        bool hasBox;
        SightBox box;
        float releasedAt;
        Vector3 releasedPoint;

        public PinchTracker(PinchTuning tuning) => this.tuning = tuning;

        public PinchReading Step(in HandFrame hand)
        {
            switch (phase)
            {
                case PinchPhase.Idle: return StepIdle(hand);
                case PinchPhase.Released: return StepReleased(hand);
                default: return StepHeld(hand);
            }
        }

        PinchReading StepIdle(in HandFrame hand)
        {
            if (!hand.Tracked || !hand.Pinching) return Reading(PinchEvent.None);
            if (FacesUser(hand)) return Reading(PinchEvent.None, PinchRejection.PalmFacingUser);
            phase = PinchPhase.Pressed;
            start = hand.PinchPoint;
            lastSeen = hand.Time;
            unpinchedSince = -1f;
            hasBox = false;
            recent.Clear();
            Record(hand);
            return Reading(PinchEvent.None);
        }

        PinchReading StepHeld(in HandFrame hand)
        {
            if (!hand.Tracked)
            {
                if (ReleaseDue(hand.Time)) return Release();
                if (hand.Time - lastSeen > tuning.lostSeconds) return End(PinchEvent.Cancelled, PinchRejection.Lost);
                return Reading(PinchEvent.None);
            }
            lastSeen = hand.Time;

            if (!hand.Pinching)
            {
                if (unpinchedSince < 0f) unpinchedSince = hand.Time;
                return ReleaseDue(hand.Time) ? Release() : Reading(PinchEvent.None);
            }
            unpinchedSince = -1f;
            Record(hand);

            if (phase == PinchPhase.Pressed)
            {
                if (Vector3.Distance(hand.PinchPoint, start) < tuning.minDragMeters) return Reading(PinchEvent.None);
                phase = PinchPhase.Dragging;
            }
            return Reading(PinchEvent.None, SpanTo(Newest));
        }

        PinchReading StepReleased(in HandFrame hand)
        {
            var away = hand.Tracked ? Vector3.Distance(hand.PinchPoint, releasedPoint) : float.PositiveInfinity;
            var inTime = hand.Time - releasedAt <= tuning.rejoinSeconds + 1e-4f;
            if (inTime && hand.Tracked && hand.Pinching && away <= tuning.rejoinMeters)
            {
                phase = PinchPhase.Dragging;
                unpinchedSince = -1f;
                lastSeen = hand.Time;
                Record(hand);
                return Reading(PinchEvent.Resumed, SpanTo(Newest));
            }

            // Twice the rejoin distance, because a dropout can wander before it comes back.
            if (!inTime || (hand.Tracked && away > 2f * tuning.rejoinMeters))
            {
                var released = box;
                var releasedStart = start;
                phase = PinchPhase.Idle;
                hasBox = false;
                // The same frame may begin the next pinch.
                var next = StepIdle(hand);
                return new PinchReading(next.Phase, PinchEvent.Confirmed, next.Rejection, true, released, releasedStart);
            }
            return Reading(PinchEvent.None);
        }

        bool ReleaseDue(float time) => unpinchedSince >= 0f && time - unpinchedSince >= tuning.releaseSeconds - 1e-4f;

        PinchReading Release()
        {
            if (phase == PinchPhase.Pressed) return End(PinchEvent.Tap, PinchRejection.None);

            var rejection = SpanTo(SampleBefore(unpinchedSince - tuning.releaseLookbackSeconds));
            if (rejection != PinchRejection.None) return End(PinchEvent.Cancelled, rejection);
            var size = box.Tangent.size;
            if (size.x < tuning.minTangentSize || size.y < tuning.minTangentSize) return End(PinchEvent.Cancelled, PinchRejection.TooSmall);
            if (Mathf.Max(size.x / size.y, size.y / size.x) > tuning.maxAspect) return End(PinchEvent.Cancelled, PinchRejection.TooNarrow);

            phase = PinchPhase.Released;
            releasedAt = unpinchedSince;
            releasedPoint = Newest.Point;
            return Reading(PinchEvent.Completed);
        }

        PinchReading End(PinchEvent pinchEvent, PinchRejection rejection)
        {
            phase = PinchPhase.Idle;
            hasBox = false;
            return Reading(pinchEvent, rejection);
        }

        PinchRejection SpanTo(in Sample end)
        {
            hasBox = TrySpan(end.Eye, start, end.Point, tuning.minDepthMeters, out box);
            return hasBox ? PinchRejection.None : PinchRejection.TooClose;
        }

        /// The box spanned by two world points, or false when either is nearer the eye than minDepth.
        static bool TrySpan(Pose eye, Vector3 a, Vector3 b, float minDepth, out SightBox spanned)
        {
            var toEye = Quaternion.Inverse(eye.rotation);
            var la = toEye * (a - eye.position);
            var lb = toEye * (b - eye.position);
            if (la.z < minDepth || lb.z < minDepth)
            {
                spanned = default;
                return false;
            }
            spanned = new SightBox(eye,
                Rect.MinMaxRect(
                    Mathf.Min(la.x / la.z, lb.x / lb.z), Mathf.Min(la.y / la.z, lb.y / lb.z),
                    Mathf.Max(la.x / la.z, lb.x / lb.z), Mathf.Max(la.y / la.z, lb.y / lb.z)),
                (la.z + lb.z) * 0.5f);
            return true;
        }

        Sample Newest => recent[recent.Count - 1];

        /// The newest sample at or before `time`, or the oldest kept one when the drag is younger than that.
        Sample SampleBefore(float time)
        {
            for (var i = recent.Count - 1; i >= 0; i--)
                if (recent[i].Time <= time + 1e-4f) return recent[i];
            return recent[0];
        }

        void Record(in HandFrame hand)
        {
            recent.Add(new Sample { Time = hand.Time, Point = hand.PinchPoint, Eye = hand.Eye });
            while (recent.Count > 1 && hand.Time - recent[0].Time > KeepSeconds) recent.RemoveAt(0);
        }

        bool FacesUser(in HandFrame hand) =>
            Vector3.Dot(hand.PalmNormal, (hand.EyePosition - hand.Wrist).normalized) > tuning.palmFacingMax;

        PinchReading Reading(PinchEvent pinchEvent, PinchRejection rejection = PinchRejection.None) =>
            new PinchReading(phase, pinchEvent, rejection, hasBox, box, start);
    }
}
