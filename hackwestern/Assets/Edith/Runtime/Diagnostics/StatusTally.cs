using Edith.Grab;
using Edith.Pinch;

namespace Edith.Diagnostics
{
    /// What the status panel remembers from the pinch and grab readings: the phases, the last rejection and when it
    /// happened, and the counts. A Look counts when its release is confirmed, which is when its snapshot hangs.
    public sealed class StatusTally
    {
        public PinchPhase PinchPhase { get; private set; }
        public PinchRejection LastRejection { get; private set; }
        public float RejectedAt { get; private set; }
        public int Taps { get; private set; }
        public int Looks { get; private set; }
        public GrabPhase GrabPhase { get; private set; }
        public int GrabCard { get; private set; } = GrabTracker.NoCard;
        public GrabEvent LastGrab { get; private set; }

        public void OnPinch(in PinchReading reading, float now)
        {
            PinchPhase = reading.Phase;
            if (reading.Event == PinchEvent.Tap) Taps++;
            if (reading.Event == PinchEvent.Confirmed) Looks++;
            if (reading.Rejection == PinchRejection.None) return;
            LastRejection = reading.Rejection;
            RejectedAt = now;
        }

        public void OnGrab(in GrabReading reading)
        {
            GrabPhase = reading.Phase;
            GrabCard = reading.CardId;
            if (reading.Event != GrabEvent.None) LastGrab = reading.Event;
        }
    }
}
