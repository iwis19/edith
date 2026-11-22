using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Edith.Scan;

namespace Edith.Ask
{
    /// What EDITH hears back from the hand actions she runs for the person: scan, collect_snapshot and
    /// clear_snapshots. Each is a JSON object; a failure is {"error":{"code","message"}}.
    public static class HandReplies
    {
        /// The longest a scan may keep the question open. The scan's own reply limit is 8 s, and the capture comes
        /// before it.
        public static readonly TimeSpan ScanLimit = TimeSpan.FromSeconds(15);

        public static readonly string ScanTimedOut = Error("scan_timeout", "The scan took too long.");
        public static readonly string NothingInView = Error("nothing_in_view", "No snapshot is near the middle of the view.");
        public static readonly string StillLooking =
            Error("still_looking", "That snapshot is still being looked at, so it can't be collected yet.");

        public static string Error(string code, string message) =>
            "{\"error\":{\"code\":" + LiveRequest.Quote(code) + ",\"message\":" + LiveRequest.Quote(message) + "}}";

        /// {"pins":[{"name","hook"}]} for the pins a scan placed, or the error that says why there are none.
        public static string Scanned(in ScanOutcome outcome)
        {
            switch (outcome.End)
            {
                case ScanEnd.Busy:
                    return Error("scan_busy", "A scan is already running.");
                case ScanEnd.Failed:
                    return Error("scan_failed", outcome.Message ?? "The scan failed.");
            }
            if (outcome.Pins.Length == 0) return Error("nothing_found", "Nothing stands out here.");
            var json = new StringBuilder("{\"pins\":[");
            for (var i = 0; i < outcome.Pins.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"name\":").Append(LiveRequest.Quote(outcome.Pins[i].Name))
                    .Append(",\"hook\":").Append(LiveRequest.Quote(outcome.Pins[i].Hook)).Append('}');
            }
            return json.Append("]}").ToString();
        }

        public static string Collected(string name) => "{\"collected\":" + LiveRequest.Quote(name ?? "") + "}";

        public const string Opened = "{\"opened\":true}", Closed = "{\"closed\":true}";
        public static readonly string AlreadyOpen = Error("already_open", "The Scrapbook is already open.");
        public static readonly string NotOpen = Error("not_open", "The Scrapbook is not open.");

        public static string Cleared(int count) => "{\"cleared\":" + count.ToString(CultureInfo.InvariantCulture) + "}";

        /// `reply`'s result, or `onTimeout` once `limit` passes first, so a call that never ends cannot hold the
        /// question open.
        public static async Task<string> WithTimeout(Task<string> reply, TimeSpan limit, string onTimeout)
        {
            var first = await Task.WhenAny(reply, Task.Delay(limit)).ConfigureAwait(false);
            return first == reply ? await reply.ConfigureAwait(false) : onTimeout;
        }
    }
}
