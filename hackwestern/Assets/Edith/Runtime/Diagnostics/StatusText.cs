using System.Text;
using Edith.Capture;
using Edith.Grab;
using Edith.Pairing;
using Edith.Pinch;

namespace Edith.Diagnostics
{
    public enum BackendReach { Unknown, Up, Down }

    /// The newest answer from the backend's /health.
    public readonly struct BackendHealth
    {
        public readonly BackendReach Reach;
        /// "85 ms" when up; what went wrong when down.
        public readonly string Detail;
        /// Seconds on the panel's clock when the answer came.
        public readonly float At;

        public BackendHealth(BackendReach reach, string detail, float at)
        {
            Reach = reach;
            Detail = detail;
            At = at;
        }
    }

    /// Everything the status panel shows, gathered from the live components.
    public struct StatusView
    {
        public StatusTally Tally;
        public int Snapshots;
        public CameraStatus Camera;
        /// False when the build has no backend, so the pairing panel never started.
        public bool HasPairing;
        public PairPhase Pairing;
        public PairHideReason PairingHidden;
        public string ScanResult;
        public string BackendHost;
        public BackendHealth Backend;
        public string LastError;
        public float Now;
    }

    public static class StatusText
    {
        const int ErrorChars = 52;

        public static string Format(in StatusView view)
        {
            var tally = view.Tally;
            var text = new StringBuilder();
            text.Append("pinch    ").Append(tally.PinchPhase).Append(", ").Append(Rejection(tally, view.Now)).Append('\n');
            text.Append("grab     ").Append(Grab(tally)).Append('\n');
            text.Append("looks ").Append(tally.Looks).Append("  taps ").Append(tally.Taps)
                .Append("  hung ").Append(view.Snapshots).Append('\n');
            text.Append("camera   ").Append(view.Camera).Append('\n');
            text.Append("phone    ").Append(Pairing(view)).Append('\n');
            text.Append("scan     ").Append(string.IsNullOrEmpty(view.ScanResult) ? "none yet" : view.ScanResult).Append('\n');
            text.Append("backend  ").Append(Backend(view)).Append('\n');
            text.Append("error    ").Append(string.IsNullOrEmpty(view.LastError) ? "none" : Brief(view.LastError, ErrorChars));
            return text.ToString();
        }

        /// The first line of `message`, cut to `max` characters with an ellipsis.
        public static string Brief(string message, int max)
        {
            var end = message.IndexOfAny(new[] { '\r', '\n' });
            var line = (end < 0 ? message : message.Substring(0, end)).Trim();
            return line.Length <= max ? line : line.Substring(0, max - 3) + "...";
        }

        static string Rejection(StatusTally tally, float now) =>
            tally.LastRejection == PinchRejection.None
                ? "no rejection"
                : $"{tally.LastRejection} {(int)(now - tally.RejectedAt)} s ago";

        static string Grab(StatusTally tally)
        {
            var phase = tally.GrabPhase == GrabPhase.Holding ? $"Holding card {tally.GrabCard}" : "Idle";
            return tally.LastGrab == GrabEvent.None ? phase : $"{phase}, last {tally.LastGrab}";
        }

        static string Pairing(in StatusView view)
        {
            if (!view.HasPairing) return "off, no backend";
            switch (view.Pairing)
            {
                case PairPhase.Checking: return "checking for a kept location";
                case PairPhase.Requesting: return "requesting a code";
                case PairPhase.Showing: return "code on screen";
                case PairPhase.Connected: return "phone connected";
            }
            switch (view.PairingHidden)
            {
                case PairHideReason.HasLocation: return "location kept";
                case PairHideReason.Done: return "paired";
                case PairHideReason.TimedOut: return "not paired, timed out";
                default: return "not paired, backend errors";
            }
        }

        static string Backend(in StatusView view)
        {
            var host = string.IsNullOrEmpty(view.BackendHost) ? "no url" : view.BackendHost;
            var health = view.Backend;
            if (health.Reach == BackendReach.Unknown) return $"{host}, checking";
            var ago = (int)(view.Now - health.At);
            return $"{host} {(health.Reach == BackendReach.Up ? "up" : "DOWN")}, {health.Detail}, {ago} s ago";
        }
    }
}
