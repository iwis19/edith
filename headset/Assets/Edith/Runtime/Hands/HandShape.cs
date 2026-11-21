using UnityEngine;

namespace Edith.Hands
{
    /// Hand shapes read from finger extension: each fingertip's distance from the wrist over its knuckle's.
    public static class HandShape
    {
        /// Above this a finger is out, below CurledMax it is curled.
        public const float OutMin = 1.6f, CurledMax = 1.35f;

        /// A peace sign: index and middle fingers out, ring finger curled. Finger guns curl the middle finger and an
        /// open hand straightens the ring finger, so neither is one.
        public static bool PeaceSign(in HandFrame hand) =>
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
