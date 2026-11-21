using Edith.Grab;
using Edith.Hands;

namespace Edith.Diagnostics
{
    /// Both hands held in closed fists for HoldSeconds show or hide the status panel. A fist that grabbed a card does
    /// not count, so carrying or pocketing a card never toggles the panel, and Scan needs both hands open. One hold
    /// toggles once; a hand has to open before the next one counts.
    public sealed class PanelToggle
    {
        public const float HoldSeconds = 1f;

        readonly GrabTuning tuning;
        float closedSince = -1f;
        bool armed = true;

        public PanelToggle(GrabTuning tuning) => this.tuning = tuning;

        /// True on the one frame the hold completes. `grabbing` is true while either fist is one that grabbed a card.
        public bool Step(in HandFrame left, in HandFrame right, bool grabbing)
        {
            if (!left.Visible || !right.Visible || grabbing)
            {
                closedSince = -1f;
                return false;
            }
            var leftCurl = GrabTracker.Curl(left);
            var rightCurl = GrabTracker.Curl(right);
            if (leftCurl > tuning.openCurl || rightCurl > tuning.openCurl)
            {
                armed = true;
                closedSince = -1f;
                return false;
            }
            if (leftCurl >= tuning.closedCurl || rightCurl >= tuning.closedCurl || !armed) return false;
            if (closedSince < 0f) closedSince = right.Time;
            if (right.Time - closedSince < HoldSeconds) return false;
            armed = false;
            closedSince = -1f;
            return true;
        }
    }
}
