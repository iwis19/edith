using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Edith.Scan
{
    /// The headset's /scan call. HttpClient rather than UnityWebRequest, which buffers responses on Quest.
    public sealed class ScanClient
    {
        // Each call is bounded by its own token.
        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        readonly string backendUrl, deviceToken, profileId;
        // One per launch: the backend never repeats a hook for the same object within it.
        readonly string session = Guid.NewGuid().ToString();

        public ScanClient(string backendUrl, string deviceToken, string profileId)
        {
            this.backendUrl = backendUrl;
            this.deviceToken = deviceToken;
            this.profileId = profileId;
        }

        /// The picks for one camera frame, most interesting first. The upload and the parse run on a worker thread.
        /// Throws HttpRequestException when the backend cannot be reached, OperationCanceledException when `token`
        /// fires, and InvalidOperationException when the backend answers with an error.
        public Task<List<ScanPick>> ScanAsync(byte[] jpeg, DateTime capturedAt, CancellationToken token) =>
            Task.Run(() => Post(jpeg, capturedAt, token), token);

        async Task<List<ScanPick>> Post(byte[] jpeg, DateTime capturedAt, CancellationToken token)
        {
            using var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(jpeg);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(image, "image", "frame.jpg");
            form.Add(new StringContent(Meta(capturedAt, session), Encoding.UTF8), "meta");

            using var request = new HttpRequestMessage(HttpMethod.Post, backendUrl + "/scan") { Content = form };
            if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
            request.Headers.Add("X-Edith-Profile", profileId);
            using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"answered {(int)response.StatusCode}: {(body.Length <= 200 ? body : body.Substring(0, 200))}");
            return ScanReply.Parse(body);
        }

        /// The meta field of a /scan request.
        public static string Meta(DateTime capturedAt, string session) =>
            $"{{\"captured_at\":\"{capturedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}\",\"session\":\"{session}\"}}";
    }
}
