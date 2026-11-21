using Edith.Hands;

namespace Edith.Pinch
{
    /// One pinch at a time across both hands, each hand on its own tracker. The hand that pinches first owns the
    /// pinch until its tap, cancel or confirmed release; the right hand wins a tie. A pinch the other hand makes
    /// meanwhile is ignored for as long as it is held, so it cannot turn into a tap or a box once the owner is done.
    public sealed class PinchPair
    {
        readonly PinchTracker[] trackers;
        readonly bool[] ignoredUntilOpen = new bool[2];
        HandSide? owner;

        public PinchPair(PinchTuning tuning) =>
            trackers = new[] { new PinchTracker(tuning), new PinchTracker(tuning) };

        /// The hand whose pinch is under way, from its first pinching frame until its outcome has been reported.
        public HandSide? Owner => owner;

        /// The owner's reading. While neither hand pinches, an idle reading that carries either hand's rejection.
        public PinchReading Step(in HandFrame right, in HandFrame left)
        {
            if (owner is HandSide held)
            {
                var ownerIsRight = held == HandSide.Right;
                Ignore(ownerIsRight ? HandSide.Left : HandSide.Right, ownerIsRight ? left : right);
                var reading = trackers[(int)held].Step(ownerIsRight ? right : left);
                if (reading.Phase == PinchPhase.Idle) owner = null;
                return reading;
            }

            var rightReading = StepFree(HandSide.Right, right);
            if (rightReading.Phase != PinchPhase.Idle)
            {
                owner = HandSide.Right;
                Ignore(HandSide.Left, left);
                return rightReading;
            }
            var leftReading = StepFree(HandSide.Left, left);
            if (leftReading.Phase != PinchPhase.Idle) owner = HandSide.Left;
            return owner == null && rightReading.Rejection != PinchRejection.None ? rightReading : leftReading;
        }

        PinchReading StepFree(HandSide side, HandFrame hand)
        {
            if (hand.Tracked && !hand.Pinching) ignoredUntilOpen[(int)side] = false;
            if (ignoredUntilOpen[(int)side]) hand.Pinching = false;
            return trackers[(int)side].Step(hand);
        }

        void Ignore(HandSide side, in HandFrame hand)
        {
            if (hand.Pinching) ignoredUntilOpen[(int)side] = true;
            else if (hand.Tracked) ignoredUntilOpen[(int)side] = false;
        }
    }
}
