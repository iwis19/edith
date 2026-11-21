using System.Collections.Generic;
using Edith.Hands;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Grab
{
    public enum GrabPhase { Idle, Holding }

    /// Non-None on exactly one frame per outcome. Dropped means the hand opened and the card floats back.
    public enum GrabEvent { None, Grabbed, Pocketed, Dropped }

    public readonly struct GrabReading
    {
        public readonly GrabPhase Phase;
        public readonly GrabEvent Event;
        /// The card held, or the card the event is about; -1 for none.
        public readonly int CardId;
        /// False while the hand is not visible.
        public readonly bool HasGrip;
        public readonly Vector3 Grip;
        /// How the hand is turned: forward along the palm toward the middle finger, up out of the palm.
        public readonly Quaternion Rotation;
        /// True from the grab until the hand opens, so a fist that has pocketed a card still is not a pinch.
        public readonly bool Claimed;

        public GrabReading(GrabPhase phase, GrabEvent grabEvent, int cardId, bool hasGrip, Vector3 grip, Quaternion rotation, bool claimed)
        {
            Phase = phase;
            Event = grabEvent;
            CardId = cardId;
            HasGrip = hasGrip;
            Grip = grip;
            Rotation = rotation;
            Claimed = claimed;
        }
    }

    /// Right-hand fist grab on cards, carry and pull to the chest. Deterministic in HandFrame.Time.
    public sealed class GrabTracker
    {
        public const int NoCard = -1;

        readonly GrabTuning tuning;
        GrabPhase phase;
        bool claimed;
        bool closed;
        float openAt = float.NegativeInfinity;
        float lastVisible;
        int cardId = NoCard;
        float startReach;
        float pullNeeded;
        bool hasGrip;
        Vector3 grip;
        Quaternion rotation = Quaternion.identity;

        public GrabTracker(GrabTuning tuning) => this.tuning = tuning;

        /// Whether the hand is closed, with the curl of the newest frame it was visible and the lowest curl since it closed.
        public bool IsClosed => closed;
        public float LastCurl { get; private set; }
        public float DeepestCurl { get; private set; }

        public GrabReading Step(in HandFrame hand, IReadOnlyList<(int id, SightBox box)> cards)
        {
            // Visible, not Tracked: a fist usually fails Tracked's high confidence on the thumb and index.
            if (!hand.Visible) return StepLost(hand);
            lastVisible = hand.Time;
            hasGrip = true;
            grip = Vector3.Lerp(hand.Wrist, hand.MiddleKnuckle, tuning.gripAlongPalm);
            rotation = Turn(hand);

            var wasClosed = closed;
            var curl = Curl(hand);
            LastCurl = curl;
            if (curl > tuning.openCurl)
            {
                closed = false;
                openAt = hand.Time;
            }
            else if (curl < tuning.closedCurl)
            {
                closed = true;
            }
            if (closed) DeepestCurl = wasClosed ? Mathf.Min(DeepestCurl, curl) : curl;

            if (wasClosed && !closed) return Opened();
            if (!wasClosed && closed) return Closed(hand, cards);
            if (closed && phase == GrabPhase.Holding) return StepHolding(hand);
            return Reading(GrabEvent.None, cardId);
        }

        GrabReading StepLost(in HandFrame hand)
        {
            hasGrip = false;
            if (!claimed || hand.Time - lastVisible <= tuning.graceSeconds) return Reading(GrabEvent.None, cardId);

            var dropped = phase == GrabPhase.Holding;
            var card = cardId;
            phase = GrabPhase.Idle;
            claimed = false;
            closed = false;
            openAt = float.NegativeInfinity;
            cardId = NoCard;
            return Reading(dropped ? GrabEvent.Dropped : GrabEvent.None, dropped ? card : NoCard);
        }

        GrabReading Opened()
        {
            var dropped = phase == GrabPhase.Holding;
            var card = cardId;
            phase = GrabPhase.Idle;
            claimed = false;
            cardId = NoCard;
            return Reading(dropped ? GrabEvent.Dropped : GrabEvent.None, dropped ? card : NoCard);
        }

        GrabReading Closed(in HandFrame hand, IReadOnlyList<(int id, SightBox box)> cards)
        {
            var deliberate = hand.Time - openAt <= tuning.openWithinSeconds + 1e-4f;
            if (!deliberate || !TryTouch(cards, out var touched)) return Reading(GrabEvent.None, cardId);

            phase = GrabPhase.Holding;
            claimed = true;
            cardId = touched;
            startReach = Reach(hand);
            pullNeeded = Mathf.Clamp(tuning.pullFraction * startReach, tuning.pullMinMeters, tuning.pullMaxMeters);
            return Reading(GrabEvent.Grabbed, touched);
        }

        GrabReading StepHolding(in HandFrame hand)
        {
            if (startReach - Reach(hand) < pullNeeded) return Reading(GrabEvent.None, cardId);

            var card = cardId;
            phase = GrabPhase.Idle;
            cardId = NoCard;
            return Reading(GrabEvent.Pocketed, card);
        }

        /// The card whose plane the grip is nearest to, among those it is beside.
        bool TryTouch(IReadOnlyList<(int id, SightBox box)> cards, out int touched)
        {
            touched = NoCard;
            var nearest = float.PositiveInfinity;
            var margin = tuning.touchMeters;
            for (var i = 0; i < cards.Count; i++)
            {
                var (id, box) = cards[i];
                var local = Quaternion.Inverse(box.Eye.rotation) * (grip - box.Eye.position);
                var offset = Mathf.Abs(local.z - box.Depth);
                if (offset > tuning.touchDepthMeters || offset >= nearest) continue;
                if (local.x < box.Tangent.xMin * box.Depth - margin || local.x > box.Tangent.xMax * box.Depth + margin) continue;
                if (local.y < box.Tangent.yMin * box.Depth - margin || local.y > box.Tangent.yMax * box.Depth + margin) continue;
                nearest = offset;
                touched = id;
            }
            return touched != NoCard;
        }

        float Reach(in HandFrame hand) =>
            Vector3.Distance(grip, hand.EyePosition + Vector3.down * tuning.pocketBelowEyeMeters);

        /// The mean fingertip-to-wrist over knuckle-to-wrist distance of the index, middle and ring fingers.
        public static float Curl(in HandFrame hand) =>
            (Extension(hand.Wrist, hand.IndexKnuckle, hand.IndexTip)
                + Extension(hand.Wrist, hand.MiddleKnuckle, hand.MiddleTip)
                + Extension(hand.Wrist, hand.RingKnuckle, hand.RingTip)) / 3f;

        /// Fingertip distance over knuckle distance from the wrist. A knuckle on the wrist counts as open.
        static float Extension(Vector3 wrist, Vector3 knuckle, Vector3 tip)
        {
            var span = Vector3.Distance(knuckle, wrist);
            return span < 1e-4f ? 2f : Vector3.Distance(tip, wrist) / span;
        }

        static Quaternion Turn(in HandFrame hand)
        {
            var forward = hand.MiddleKnuckle - hand.Wrist;
            return forward.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.LookRotation(forward, hand.PalmNormal);
        }

        GrabReading Reading(GrabEvent grabEvent, int card) =>
            new GrabReading(phase, grabEvent, card, hasGrip, grip, rotation, claimed);
    }
}
