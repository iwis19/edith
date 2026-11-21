using Edith.Hands;
using UnityEngine;

namespace Edith.Welcome
{
    /// The gesture that brings the welcome back: a peace sign on both hands, index and middle fingers out and the ring
    /// finger curled, held for HoldSeconds. It fires once per hold; the hands leave the pose before it can fire again.
    /// Finger guns curl the middle finger and an open hand straightens the ring finger, so neither passes.
    public sealed class PeaceSignsTracker
    {
        public const float HoldSeconds = 1.5f;
        /// Fingertip-to-wrist over knuckle-to-wrist: above OutMin a finger is out, below CurledMax it is curled.
        public const float OutMin = 1.6f, CurledMax = 1.35f;
        /// A dropout shorter than this keeps the hold going, since tracking blinks.
        public const float GraceSeconds = 0.2f;

        bool holding, fired;
        float heldSince, lastPosed;

        /// True on the one frame the hold completes.
        public bool Step(in HandFrame left, in HandFrame right)
        {
            var time = Mathf.Max(left.Time, right.Time);
            if (Peace(left) && Peace(right))
            {
                if (!holding)
                {
                    holding = true;
                    heldSince = time;
                }
                lastPosed = time;
                if (fired || time - heldSince < HoldSeconds) return false;
                fired = true;
                return true;
            }
            if (holding && time - lastPosed > GraceSeconds) holding = fired = false;
            return false;
        }

        public static bool Peace(in HandFrame hand) =>
            hand.Tracked
            && Extension(hand.Wrist, hand.IndexKnuckle, hand.IndexTip) > OutMin
            && Extension(hand.Wrist, hand.MiddleKnuckle, hand.MiddleTip) > OutMin
            && Extension(hand.Wrist, hand.RingKnuckle, hand.RingTip) < CurledMax;

        static float Extension(Vector3 wrist, Vector3 knuckle, Vector3 tip)
        {
            var span = Vector3.Distance(knuckle, wrist);
            return span < 1e-4f ? 0f : Vector3.Distance(tip, wrist) / span;
        }
    }
}
