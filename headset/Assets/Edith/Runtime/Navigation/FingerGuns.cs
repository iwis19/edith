using System;
using Edith.Hands;
using UnityEngine;

namespace Edith.Navigation
{
    /// Which way one hand's finger gun points, if it is making one.
    public enum GunPose { None, Forward, Up }

    /// What both hands together asked for.
    public enum NavGesture { None, Start, Stop }

    /// Thresholds for reading a finger gun off the hand skeleton. Distances are in metres, angles in degrees.
    [Serializable]
    public sealed class FingerGunTuning
    {
        /// The index tip must reach this much farther from the wrist than its knuckle: a straight finger.
        public float IndexReach = 0.05f;
        /// The middle and ring tips may reach at most this much past their knuckles: curled into the palm.
        public float CurledReach = 0.035f;
        /// The thumb tip must stand this far from the index knuckle: cocked up, not tucked along the finger.
        public float ThumbSpread = 0.035f;
        /// How far the index may point from straight ahead (level, the way the head faces) or from straight up.
        public float ForwardDegrees = 40f, UpDegrees = 35f;
        /// Both hands must hold the same pose this long before it counts.
        public float HoldSeconds = 0.3f;
        /// After a gesture fires, another waits this long, and both hands must first let go of the pose.
        public float CooldownSeconds = 1.5f;
    }

    public static class FingerGuns
    {
        /// The pose one hand makes: index straight, middle and ring curled, thumb cocked, and the index pointing ahead
        /// or up. An unseen hand makes none.
        public static GunPose Classify(in HandFrame hand, FingerGunTuning tuning)
        {
            if (!hand.Visible) return GunPose.None;
            var wrist = hand.Wrist;
            if (Reach(wrist, hand.IndexKnuckle, hand.IndexTip) < tuning.IndexReach) return GunPose.None;
            if (Reach(wrist, hand.MiddleKnuckle, hand.MiddleTip) > tuning.CurledReach) return GunPose.None;
            if (Reach(wrist, hand.RingKnuckle, hand.RingTip) > tuning.CurledReach) return GunPose.None;
            if (Vector3.Distance(hand.ThumbTip, hand.IndexKnuckle) < tuning.ThumbSpread) return GunPose.None;

            var pointing = hand.IndexTip - hand.IndexKnuckle;
            if (pointing.sqrMagnitude < 1e-6f) return GunPose.None;
            if (Vector3.Angle(pointing, Vector3.up) <= tuning.UpDegrees) return GunPose.Up;
            var ahead = Vector3.ProjectOnPlane(hand.EyeRotation * Vector3.forward, Vector3.up);
            if (ahead.sqrMagnitude > 1e-6f && Vector3.Angle(pointing, ahead) <= tuning.ForwardDegrees) return GunPose.Forward;
            return GunPose.None;
        }

        /// How much farther from the wrist a fingertip is than its knuckle.
        static float Reach(Vector3 wrist, Vector3 knuckle, Vector3 tip) =>
            Vector3.Distance(wrist, tip) - Vector3.Distance(wrist, knuckle);
    }

    /// Both hands' finger guns over time: two guns pointing ahead start directions, two pointing up stop them. A pose
    /// must be held by both hands for HoldSeconds and fires once. It fires again only after the hands have left that
    /// pose for ReleaseSeconds, so a tracking flicker mid-hold does not fire twice, and never within the cooldown.
    /// Starting also needs both hands to have dropped the finger guns entirely for ReleaseSeconds since the last
    /// gesture: lowering the hands after pointing up passes through pointing ahead, and that must not start again.
    public sealed class FingerGunPair
    {
        public const float ReleaseSeconds = 0.25f;

        readonly FingerGunTuning tuning;
        GunPose holding, fired;
        float heldSince, firedSeenAt;
        bool latched;
        float firedAt = float.NegativeInfinity;
        // Both hands showed no finger gun for ReleaseSeconds since the last gesture fired.
        bool dropped = true;
        float noGunSince = float.NaN;

        public FingerGunPair(FingerGunTuning tuning) => this.tuning = tuning;

        public NavGesture Step(in HandFrame right, in HandFrame left)
        {
            var r = FingerGuns.Classify(right, tuning);
            var l = FingerGuns.Classify(left, tuning);
            var both = r == l ? r : GunPose.None;
            var now = Math.Max(right.Time, left.Time);
            if (both != holding)
            {
                holding = both;
                heldSince = now;
            }
            if (r == GunPose.None && l == GunPose.None)
            {
                if (float.IsNaN(noGunSince)) noGunSince = now;
                if (now - noGunSince >= ReleaseSeconds) dropped = true;
            }
            else noGunSince = float.NaN;
            if (latched)
            {
                if (both == fired) firedSeenAt = now;
                else if (now - firedSeenAt >= ReleaseSeconds) latched = false;
            }
            if (latched || both == GunPose.None || now - heldSince < tuning.HoldSeconds
                || now - firedAt < tuning.CooldownSeconds || (both == GunPose.Forward && !dropped)) return NavGesture.None;
            latched = true;
            dropped = false;
            fired = both;
            firedAt = firedSeenAt = now;
            return both == GunPose.Forward ? NavGesture.Start : NavGesture.Stop;
        }
    }
}
