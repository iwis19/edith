using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Edith.Ask
{
    public enum LiveEventKind { Ready, Audio, InputText, OutputText, Sources, TurnComplete, Interrupted, Closed, Failed, ToolCall, ToolCancelled }

    public readonly struct LiveEvent
    {
        public readonly LiveEventKind Kind;
        /// The new piece of transcript, for InputText and OutputText; the source names one per line, for Sources.
        public readonly string Text;
        /// Why, for Failed.
        public readonly AskProblem Problem;
        /// The function to run, for ToolCall. For ToolCancelled, `Text` is the id of the call to drop.
        public readonly LiveToolCall Call;

        public LiveEvent(LiveEventKind kind, string text = null, AskProblem problem = AskProblem.None, LiveToolCall call = null)
        {
            Kind = kind;
            Text = text;
            Problem = problem;
            Call = call;
        }
    }

    /// One Gemini Live session for one question: minted by the backend's POST /ask/session, then a websocket straight
    /// to Gemini. Every network step runs on worker threads. The answer's audio goes straight into `speech`, and
    /// everything else waits in a queue the main thread drains with TryNext. Anything sent before the session is
    /// ready waits for it.
    public sealed class LiveLink : IDisposable
    {
        public const int MicRate = 16000;
        /// About five seconds of microphone audio can wait for the session.
        public const int MaxQueuedAudio = 125;
        const int ReceiveBufferBytes = 64 * 1024;

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        sealed class Failure : Exception
        {
            public readonly AskProblem Problem;

            public Failure(AskProblem problem, string detail) : base(detail) => Problem = problem;
        }

        readonly PcmRing.Feed speech;
        readonly int chunkSamples;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly ConcurrentQueue<LiveEvent> events = new ConcurrentQueue<LiveEvent>();
        readonly LiveOutbox outbox = new LiveOutbox(MaxQueuedAudio);
        readonly SemaphoreSlim waiting = new SemaphoreSlim(0);
        readonly ConcurrentBag<float[]> chunks = new ConcurrentBag<float[]>();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        bool droppedAudio;

        LiveLink(PcmRing speech, int chunkSamples)
        {
            this.speech = speech.OpenFeed();
            this.chunkSamples = chunkSamples;
        }

        /// Starts minting and connecting at once and returns without waiting.
        public static LiveLink Open(string backendUrl, string deviceToken, string profileId, AskHint? hint, PcmRing speech,
            int chunkSamples)
        {
            var link = new LiveLink(speech, chunkSamples);
            var token = link.cancel.Token;
            var requestBody = AskHint.RequestBody(hint);
            Task.Run(() => link.Run(backendUrl, deviceToken, profileId, requestBody, token));
            return link;
        }

        public bool TryNext(out LiveEvent next) => events.TryDequeue(out next);

        /// An array of the chunk size to fill and hand to SendAudio, which takes it back.
        public float[] RentChunk() => chunks.TryTake(out var chunk) ? chunk : new float[chunkSamples];

        /// Hands back a rented array that was not sent.
        public void ReturnChunk(float[] chunk) => chunks.Add(chunk);

        /// Past MaxQueuedAudio chunks waiting, the oldest is dropped.
        public void SendAudio(float[] chunk)
        {
            if (cancel.IsCancellationRequested)
            {
                chunks.Add(chunk);
                return;
            }
            if (outbox.AddAudio(chunk) is { } dropped)
            {
                chunks.Add(dropped);
                if (!droppedAudio) Debug.Log("[EDITH ask] the session is slow to open, dropping the oldest microphone audio");
                droppedAudio = true;
            }
            waiting.Release();
        }

        /// Answers a function call EDITH made, so it can speak the result. Sent in order with the rest of what goes out.
        public void SendToolResponse(string id, string name, string responseJson) =>
            EnqueueText(LiveRequest.ToolResponse(id, name, responseJson));

        public void SendImage(byte[] jpeg) => EnqueueText(LiveRequest.Image(jpeg));

        /// Tells Gemini the microphone has stopped.
        public void EndAudio() => EnqueueText(LiveRequest.AudioStreamEnd);

        /// Closes the session. Events still queued are dropped.
        public void Dispose() => cancel.Cancel();

        void EnqueueText(string json)
        {
            if (cancel.IsCancellationRequested) return;
            outbox.AddText(json);
            waiting.Release();
        }

        void Post(in LiveEvent e)
        {
            if (!cancel.IsCancellationRequested) events.Enqueue(e);
        }

        async Task Run(string backendUrl, string deviceToken, string profileId, string requestBody, CancellationToken token)
        {
            var socket = new ClientWebSocket();
            var ready = false;
            try
            {
                var grant = await Mint(backendUrl, deviceToken, profileId, requestBody, token).ConfigureAwait(false);
                Debug.Log($"[EDITH ask] backend minted a {grant.Model} session in {clock.ElapsedMilliseconds} ms");
                try
                {
                    await socket.ConnectAsync(new Uri(grant.Url), token).ConfigureAwait(false);
                }
                catch (Exception e) when (!token.IsCancellationRequested && !(e is OperationCanceledException))
                {
                    throw new Failure(AskProblem.Unreachable, $"websocket: {e.GetBaseException().Message}");
                }
                await SendText(socket, grant.SetupFrame, token).ConfigureAwait(false);
                Debug.Log($"[EDITH ask] websocket open {clock.ElapsedMilliseconds} ms after the tap");
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

        static async Task<AskGrant> Mint(string backendUrl, string deviceToken, string profileId, string requestBody,
            CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, backendUrl + "/ask/session")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
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
                    speech.Finish();
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
                Debug.Log($"[EDITH ask] setupComplete {clock.ElapsedMilliseconds} ms after the tap");
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
            foreach (var call in message.ToolCalls)
            {
                Debug.Log($"[EDITH ask] EDITH called {call.Name}");
                Post(new LiveEvent(LiveEventKind.ToolCall, call: call));
            }
            foreach (var id in message.CancelledCalls) Post(new LiveEvent(LiveEventKind.ToolCancelled, id));
            if (message.OutputText.Length > 0) Post(new LiveEvent(LiveEventKind.OutputText, message.OutputText));
            if (message.Sources.Count > 0) Post(new LiveEvent(LiveEventKind.Sources, string.Join("\n", message.Sources)));
            if (message.Interrupted)
            {
                speech.Drop();
                Post(new LiveEvent(LiveEventKind.Interrupted));
            }
            if (message.TurnComplete)
            {
                speech.Finish();
                Post(new LiveEvent(LiveEventKind.TurnComplete));
            }
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
                    if (!outbox.TryTake(out var audio, out var json)) continue;
                    if (audio != null)
                    {
                        var bytes = Pcm16.Write(audio, audio.Length, pcm);
                        chunks.Add(audio);
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

    /// Messages waiting to go out on a Live session, taken in the order they were added. At most `maxAudio`
    /// microphone chunks wait: one more drops the oldest, so the latest words of a question outlast a slow connect.
    public sealed class LiveOutbox
    {
        readonly int maxAudio;
        readonly object gate = new object();
        readonly Queue<(long order, float[] chunk)> audio = new Queue<(long order, float[] chunk)>();
        readonly Queue<(long order, string json)> text = new Queue<(long order, string json)>();
        long added;

        public LiveOutbox(int maxAudio) => this.maxAudio = maxAudio;

        /// Returns the chunk dropped to make room, or null.
        public float[] AddAudio(float[] chunk)
        {
            lock (gate)
            {
                audio.Enqueue((added++, chunk));
                return audio.Count > maxAudio ? audio.Dequeue().chunk : null;
            }
        }

        public void AddText(string json)
        {
            lock (gate) text.Enqueue((added++, json));
        }

        /// The oldest message waiting: a microphone chunk in `chunk`, or a JSON message in `json`. False when none is.
        public bool TryTake(out float[] chunk, out string json)
        {
            lock (gate)
            {
                chunk = null;
                json = null;
                if (audio.Count == 0 && text.Count == 0) return false;
                if (text.Count == 0 || (audio.Count > 0 && audio.Peek().order < text.Peek().order)) chunk = audio.Dequeue().chunk;
                else json = text.Dequeue().json;
                return true;
            }
        }
    }
}
