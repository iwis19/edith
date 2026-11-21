using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Edith.Ask
{
    public enum LiveEventKind { Ready, Audio, InputText, OutputText, TurnComplete, Interrupted, Closed, Failed }

    public readonly struct LiveEvent
    {
        public readonly LiveEventKind Kind;
        /// The new piece of transcript, for InputText and OutputText.
        public readonly string Text;
        /// Why, for Failed.
        public readonly AskProblem Problem;

        public LiveEvent(LiveEventKind kind, string text = null, AskProblem problem = AskProblem.None)
        {
            Kind = kind;
            Text = text;
            Problem = problem;
        }
    }

    /// One Gemini Live session for one question: minted by the backend's POST /ask/session, then a websocket straight
    /// to Gemini. Every network step runs on worker threads. The answer's audio goes straight into `speech`, and
    /// everything else waits in a queue the main thread drains with TryNext. Anything sent before the session is
    /// ready waits for it.
    public sealed class LiveLink : IDisposable
    {
        public const int MicRate = 16000;
        // About five seconds of microphone audio can wait for the session; later chunks are dropped.
        const int MaxQueuedAudio = 125;
        const int ReceiveBufferBytes = 64 * 1024;

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        sealed class Outgoing
        {
            public float[] Audio;
            public string Text;
        }

        sealed class Failure : Exception
        {
            public readonly AskProblem Problem;

            public Failure(AskProblem problem, string detail) : base(detail) => Problem = problem;
        }

        readonly PcmRing speech;
        readonly int chunkSamples;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly ConcurrentQueue<LiveEvent> events = new ConcurrentQueue<LiveEvent>();
        readonly ConcurrentQueue<Outgoing> outbox = new ConcurrentQueue<Outgoing>();
        readonly SemaphoreSlim waiting = new SemaphoreSlim(0);
        readonly ConcurrentBag<float[]> chunks = new ConcurrentBag<float[]>();
        int queuedAudio;

        LiveLink(PcmRing speech, int chunkSamples)
        {
            this.speech = speech;
            this.chunkSamples = chunkSamples;
        }

        /// Starts minting and connecting at once and returns without waiting.
        public static LiveLink Open(string backendUrl, string deviceToken, string profileId, PcmRing speech, int chunkSamples)
        {
            var link = new LiveLink(speech, chunkSamples);
            var token = link.cancel.Token;
            Task.Run(() => link.Run(backendUrl, deviceToken, profileId, token));
            return link;
        }

        public bool TryNext(out LiveEvent next) => events.TryDequeue(out next);

        /// An array of the chunk size to fill and hand to SendAudio, which takes it back.
        public float[] RentChunk() => chunks.TryTake(out var chunk) ? chunk : new float[chunkSamples];

        /// Hands back a rented array that was not sent.
        public void ReturnChunk(float[] chunk) => chunks.Add(chunk);

        public void SendAudio(float[] chunk)
        {
            if (Interlocked.Increment(ref queuedAudio) > MaxQueuedAudio)
            {
                Interlocked.Decrement(ref queuedAudio);
                chunks.Add(chunk);
                return;
            }
            Enqueue(new Outgoing { Audio = chunk });
        }

        public void SendImage(byte[] jpeg) => Enqueue(new Outgoing { Text = LiveRequest.Image(jpeg) });

        /// Tells Gemini the microphone has stopped.
        public void EndAudio() => Enqueue(new Outgoing { Text = LiveRequest.AudioStreamEnd });

        /// Closes the session. Events still queued are dropped.
        public void Dispose() => cancel.Cancel();

        void Enqueue(Outgoing item)
        {
            if (cancel.IsCancellationRequested) return;
            outbox.Enqueue(item);
            waiting.Release();
        }

        void Post(in LiveEvent e)
        {
            if (!cancel.IsCancellationRequested) events.Enqueue(e);
        }

        async Task Run(string backendUrl, string deviceToken, string profileId, CancellationToken token)
        {
            var socket = new ClientWebSocket();
            var ready = false;
            try
            {
                var started = DateTime.UtcNow;
                var grant = await Mint(backendUrl, deviceToken, profileId, token).ConfigureAwait(false);
                try
                {
                    await socket.ConnectAsync(new Uri(grant.Url), token).ConfigureAwait(false);
                }
                catch (Exception e) when (!token.IsCancellationRequested && !(e is OperationCanceledException))
                {
                    throw new Failure(AskProblem.Unreachable, $"websocket: {e.GetBaseException().Message}");
                }
                await SendText(socket, grant.SetupFrame, token).ConfigureAwait(false);
                Debug.Log($"[EDITH ask] {grant.Model} session open {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms after the tap");
                await Receive(socket, token, () =>
                {
                    ready = true;
                    _ = Task.Run(() => SendLoop(socket, token));
                }).ConfigureAwait(false);
            }
            catch (Failure failure) when (!token.IsCancellationRequested)
            {
                Debug.Log($"[EDITH ask] {failure.Problem}: {failure.Message}");
                Post(new LiveEvent(LiveEventKind.Failed, problem: failure.Problem));
            }
            catch (Exception e) when (!token.IsCancellationRequested && !(e is OperationCanceledException))
            {
                Debug.Log($"[EDITH ask] connection lost: {e.GetBaseException().Message}");
                Post(new LiveEvent(LiveEventKind.Failed, problem: ready ? AskProblem.Dropped : AskProblem.Unreachable));
            }
            catch (Exception)
            {
            }
            finally
            {
                cancel.Cancel();
                // Cancelling a pending receive aborts the socket, which ends the session on Gemini's side.
                socket.Dispose();
            }
        }

        static async Task<AskGrant> Mint(string backendUrl, string deviceToken, string profileId, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, backendUrl + "/ask/session")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(deviceToken)) request.Headers.Add("X-Edith-Token", deviceToken);
            request.Headers.Add("X-Edith-Profile", profileId);
            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                throw new Failure(AskProblem.Unreachable, $"/ask/session: {e.GetBaseException().Message}");
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status >= 400 && status < 500) throw new Failure(AskProblem.Refused, $"/ask/session answered {status}: {Clip(body)}");
                if (!response.IsSuccessStatusCode) throw new Failure(AskProblem.Unreachable, $"/ask/session answered {status}: {Clip(body)}");
                if (!AskGrant.TryParse(body, out var grant)) throw new Failure(AskProblem.Refused, $"/ask/session sent no url and setup: {Clip(body)}");
                return grant;
            }
        }

        async Task Receive(ClientWebSocket socket, CancellationToken token, Action onReady)
        {
            var buffer = new byte[ReceiveBufferBytes];
            using var message = new MemoryStream();
            while (!token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log($"[EDITH ask] Gemini closed the session: {result.CloseStatus} {result.CloseStatusDescription}");
                    Post(new LiveEvent(LiveEventKind.Closed));
                    return;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                // Gemini sends its JSON in binary frames as well as text ones.
                var parsed = LiveMessage.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
                Handle(parsed, onReady);
            }
        }

        void Handle(LiveMessage message, Action onReady)
        {
            if (message.SetupComplete)
            {
                onReady();
                Post(new LiveEvent(LiveEventKind.Ready));
            }
            if (message.InputText.Length > 0) Post(new LiveEvent(LiveEventKind.InputText, message.InputText));
            if (message.Audio.Count > 0)
            {
                foreach (var pcm in message.Audio)
                    if (!speech.Write(pcm)) Debug.LogWarning("[EDITH ask] answer audio buffer full, dropping the rest");
                Post(new LiveEvent(LiveEventKind.Audio));
            }
            if (message.OutputText.Length > 0) Post(new LiveEvent(LiveEventKind.OutputText, message.OutputText));
            if (message.Interrupted)
            {
                speech.Clear();
                Post(new LiveEvent(LiveEventKind.Interrupted));
            }
            if (message.TurnComplete) Post(new LiveEvent(LiveEventKind.TurnComplete));
            if (message.GoAway) Debug.Log("[EDITH ask] Gemini will close the session soon");
        }

        async Task SendLoop(ClientWebSocket socket, CancellationToken token)
        {
            var pcm = new byte[chunkSamples * 2];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await waiting.WaitAsync(token).ConfigureAwait(false);
                    if (!outbox.TryDequeue(out var item)) continue;
                    var json = item.Text;
                    if (item.Audio != null)
                    {
                        var bytes = Pcm16.Write(item.Audio, item.Audio.Length, pcm);
                        chunks.Add(item.Audio);
                        Interlocked.Decrement(ref queuedAudio);
                        json = LiveRequest.Audio(pcm, bytes, MicRate);
                    }
                    await SendText(socket, json, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!token.IsCancellationRequested && !(e is OperationCanceledException))
            {
                Debug.Log($"[EDITH ask] send failed: {e.GetBaseException().Message}");
                Post(new LiveEvent(LiveEventKind.Failed, problem: AskProblem.Dropped));
                cancel.Cancel();
            }
            catch (Exception)
            {
            }
        }

        static Task SendText(ClientWebSocket socket, string json, CancellationToken token) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, token);

        static string Clip(string text) => text.Length <= 200 ? text : text.Substring(0, 200);
    }
}
