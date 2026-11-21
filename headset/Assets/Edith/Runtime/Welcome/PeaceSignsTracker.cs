using Edith.Hands;
using UnityEngine;

namespace Edith.Welcome
{
    /// The gesture that brings the welcome back: HandShape.PeaceSign on both hands, held for HoldSeconds. It fires
    /// once per hold; the hands leave the pose before it can fire again. Scan ignores peace signs, so the two never meet.
    public sealed class PeaceSignsTracker
    {
        public const float HoldSeconds = 1.5f;
        /// A dropout shorter than this keeps the hold going, since tracking blinks.
        public const float GraceSeconds = 0.2f;

        bool holding, fired;
        float heldSince, lastPosed;

        /// True on the one frame the hold completes.
        public bool Step(in HandFrame left, in HandFrame right)
        {
            var time = Mathf.Max(left.Time, right.Time);
            if (HandShape.PeaceSign(left) && HandShape.PeaceSign(right))
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
    }
}
