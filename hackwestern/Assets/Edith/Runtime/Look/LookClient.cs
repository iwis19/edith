using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Edith.Backend;
using Debug = UnityEngine.Debug;

namespace Edith.Look
{
    /// One POST /look for one Look. The request runs on worker threads and the main thread drains the events with
    /// TryNext. However it goes, it ends in exactly one Done or Error event, unless it is cancelled, which ends it
    /// quietly. An Error from here is synthetic when the backend never sent one: its code is `timeout`, `network`,
    /// `http_<status>`, `stream_ended`, `empty_response` or `not_configured`.
    public sealed class LookClient
    {
        const int ReadBytes = 4096;

        // HttpClient, not UnityWebRequest: on a Quest 3S UnityWebRequest handed over a streamed response only once it
        // had all arrived. One client keeps the connection open between Looks, which skips a handshake each time.
        internal static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        readonly ConcurrentQueue<LookEvent> events = new ConcurrentQueue<LookEvent>();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly int lookId;

        LookClient(int lookId) => this.lookId = lookId;

        /// Starts sending `jpeg` to the backend this build was set up with and returns without waiting.
        public static LookClient Send(int lookId, byte[] jpeg, DateTime capturedAtUtc)
        {
            var settings = BackendSettings.Load();
            if (settings == null)
            {
                var unset = new LookClient(lookId);
                unset.Fail("not_configured");
                return unset;
            }
            return Send(settings.Url, settings.DeviceToken, BackendSettings.ProfileId, lookId, jpeg, capturedAtUtc, LookTimeouts.Default);
        }

        public static LookClient Send(string url, string deviceToken, string profileId, int lookId, byte[] jpeg,
            DateTime capturedAtUtc, LookTimeouts timeouts)
        {
            var client = new LookClient(lookId);
            var meta = LookMeta.Json(lookId, capturedAtUtc);
            Task.Run(() => client.Run(url, deviceToken, profileId, jpeg, meta, timeouts));
            return client;
        }

        /// GETs `<url>/health` on the shared connection so it is open when a Look goes out: the first HTTPS
        /// connection after launch can stall long enough to time a whole Look out. Failures are logged and ignored.
        public static Task Warm(string url) => Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                using var response = await Http.GetAsync(url + "/health", timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) Debug.Log($"[EDITH look] keep-warm got http_{(int)response.StatusCode}");
            }
            catch (Exception e)
            {
                Debug.Log($"[EDITH look] keep-warm failed: {(timeout.IsCancellationRequested ? "timeout" : e.GetBaseException().Message)}");
            }
        });

        public bool TryNext(out LookEvent next)
        {
            next = default;
            return !cancel.IsCancellationRequested && events.TryDequeue(out next);
        }

        /// Stops the request. Events not yet taken are dropped.
        public void Cancel() => cancel.Cancel();

        void Post(in LookEvent e)
        {
            if (!cancel.IsCancellationRequested) events.Enqueue(e);
        }

        void Fail(string code) => Post(LookEvent.Failure(code, MessageFor(code)));

        async Task Run(string url, string token, string profile, byte[] jpeg, string meta, LookTimeouts timeouts)
        {
            var clock = Stopwatch.StartNew();
            using var total = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            total.CancelAfter(Seconds(timeouts.Total));
            Debug.Log($"[EDITH look] look {lookId} sending {jpeg.Length / 1024} KB");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var (failure, gotEvent) = await Attempt(url, token, profile, jpeg, meta, timeouts, total.Token).ConfigureAwait(false);
                if (cancel.IsCancellationRequested) return;
                if (failure == null)
                {
                    Debug.Log($"[EDITH look] look {lookId} finished after {clock.ElapsedMilliseconds} ms");
                    return;
                }
                if (attempt == 0 && !gotEvent && !total.IsCancellationRequested && IsTransient(failure))
                {
                    Debug.Log($"[EDITH look] look {lookId} retrying after {failure}");
                    continue;
                }
                Debug.Log($"[EDITH look] look {lookId} failed with {failure} after {clock.ElapsedMilliseconds} ms");
                Fail(failure);
                return;
            }
        }

        /// The failure code, or null when the stream ended with a Done or an Error.
        async Task<(string failure, bool gotEvent)> Attempt(string url, string token, string profile, byte[] jpeg, string meta,
            LookTimeouts timeouts, CancellationToken total)
        {
            var gotEvent = false;
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(total);
            phase.CancelAfter(Seconds(timeouts.Upload));
            try
            {
                using var request = Build(url + "/look", token, profile, jpeg, meta);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, phase.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return ($"http_{(int)response.StatusCode}", false);
                phase.CancelAfter(Seconds(timeouts.FirstEvent));
                // A read that is already waiting does not always stop when its token is cancelled.
                using var stopReading = phase.Token.Register(response.Dispose);
                using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var parser = new NdjsonParser();
                var lines = new List<string>();
                var buffer = new byte[ReadBytes];
                while (true)
                {
                    var read = await body.ReadAsync(buffer, 0, buffer.Length, phase.Token).ConfigureAwait(false);
                    if (read == 0) return (gotEvent ? "stream_ended" : "empty_response", gotEvent);
                    lines.Clear();
                    parser.Feed(buffer, read, lines);
                    foreach (var line in lines)
                    {
                        if (!LookEvent.TryParse(line, out var e)) continue;
                        gotEvent = true;
                        phase.CancelAfter(Timeout.InfiniteTimeSpan);
                        Post(e);
                        if (e.Ends) return (null, true);
                    }
                }
            }
            catch (Exception e)
            {
                if (phase.IsCancellationRequested) return ("timeout", gotEvent);
                Debug.Log($"[EDITH look] look {lookId} network error: {e.GetBaseException().Message}");
                return ("network", gotEvent);
            }
        }

        internal static HttpRequestMessage Build(string endpoint, string token, string profile, byte[] jpeg, string meta)
        {
            var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(jpeg);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(image, "image", "look.jpg");
            form.Add(new StringContent(meta), "meta");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
            if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Edith-Token", token);
            request.Headers.Add("X-Edith-Profile", profile);
            return request;
        }

        static bool IsTransient(string code) =>
            code == "timeout" || code == "network" || code == "empty_response" || code.StartsWith("http_5", StringComparison.Ordinal);

        static TimeSpan Seconds(float seconds) => TimeSpan.FromSeconds(seconds);

        static string MessageFor(string code)
        {
            switch (code)
            {
                case "timeout": return "EDITH took too long.";
                case "network": return "EDITH can't be reached.";
                case "not_configured": return "EDITH is not set up.";
                default: return "EDITH couldn't read that.";
            }
        }
    }
}
