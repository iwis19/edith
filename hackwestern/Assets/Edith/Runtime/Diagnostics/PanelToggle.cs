using Edith.Grab;
using Edith.Hands;

namespace Edith.Diagnostics
{
    /// A closed left fist held for HoldSeconds shows or hides the status panel. The left hand has no other job
    /// besides the palms-out Scan, which needs it open, and the right-hand pinch, grab and thumb gestures never read it.
    /// One hold toggles once; the hand has to open before the next one counts.
    public sealed class PanelToggle
    {
        public const float HoldSeconds = 1f;

        readonly GrabTuning tuning;
        float closedSince = -1f;
        bool armed = true;

        public PanelToggle(GrabTuning tuning) => this.tuning = tuning;

        /// True on the one frame the hold completes.
        public bool Step(in HandFrame left)
        {
            if (!left.Visible)
            {
                closedSince = -1f;
                return false;
            }
            var curl = GrabTracker.Curl(left);
            if (curl > tuning.openCurl)
            {
                armed = true;
                closedSince = -1f;
                return false;
            }
            if (curl >= tuning.closedCurl || !armed) return false;
            if (closedSince < 0f) closedSince = left.Time;
            if (left.Time - closedSince < HoldSeconds) return false;
            armed = false;
            closedSince = -1f;
            return true;
        }
    }
}
