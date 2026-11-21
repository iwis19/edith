using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using Edith.Look;

namespace Edith.Bounty
{
    /// What a hunt route answered. `Body` is the response text as sent, or, when the backend could not be reached, an
    /// error body of the same shape the backend's own errors have.
    public readonly struct HuntReply
    {
        public readonly int Status;
        public readonly string Body;

        public HuntReply(int status, string body)
        {
            Status = status;
            Body = body;
        }

        public bool Ok => Status >= 200 && Status < 300;
        public bool NoHunt => Status == 404;

        /// The hunt the route returned, or null when it answered with an error.
        public HuntState State => Ok && HuntState.TryParse(Body, out var state) ? state : null;

        /// What EDITH reads as the tool's result: the HuntState on success, else the error body, as a JSON object.
        public string ToolResult
        {
            get
            {
                var body = (Body ?? "").Trim();
                if (Ok && body.StartsWith("{", StringComparison.Ordinal)) return body;
                if (!Ok && body.StartsWith("{", StringComparison.Ordinal) && body.Contains("\"error\"")) return body;
                return ErrorBody("http_" + Status, "The hunt service answered with an error.");
            }
        }

        /// {"error":{"code","message"}}, the shape the backend's errors have.
        public static string ErrorBody(string code, string message) =>
            "{\"error\":{\"code\":" + JsonText.Quote(code) + ",\"message\":" + JsonText.Quote(message) + "}}";
    }

    /// The headset's calls to the Bounty routes: POST /hunt, POST /hunt/stop and GET /hunt.
    public sealed class HuntClient
    {
        const float TimeoutSeconds = 12f;

        readonly string backendUrl, deviceToken, profileId;

        public HuntClient(string backendUrl, string deviceToken, string profileId)
        {
            this.backendUrl = backendUrl;
            this.deviceToken = deviceToken;
            this.profileId = profileId;
        }

        /// The body of POST /hunt: the interests when there are any, else `{}`.
        public static string StartBody(IReadOnlyList<string> interests)
        {
            if (interests == null || interests.Count == 0) return "{}";
            var body = new StringBuilder("{\"interests\":[");
            for (var i = 0; i < interests.Count; i++)
            {
                if (i > 0) body.Append(',');
                body.Append(JsonText.Quote(interests[i]));
            }
            return body.Append("]}").ToString();
        }

        public Task<HuntReply> Start(IReadOnlyList<string> interests) => Send(HttpMethod.Post, "/hunt", StartBody(interests));

        public Task<HuntReply> Stop() => Send(HttpMethod.Post, "/hunt/stop", "{}");

        public Task<HuntReply> Get() => Send(HttpMethod.Get, "/hunt", null);

        /// Never faults: a backend that cannot be reached answers `unreachable`, and a slow one `timeout`.
        Task<HuntReply> Send(HttpMethod method, string path, string body) => Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(method, backendUrl + path);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
                request.Headers.Add("X-Edith-Profile", profileId);
                using var response = await LookClient.Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HuntReply((int)response.StatusCode, text);
            }
            catch (Exception)
            {
                var timedOut = timeout.IsCancellationRequested;
                return new HuntReply(0, HuntReply.ErrorBody(timedOut ? "timeout" : "unreachable",
                    timedOut ? "The hunt service took too long." : "The hunt service could not be reached."));
            }
        });
    }
}
