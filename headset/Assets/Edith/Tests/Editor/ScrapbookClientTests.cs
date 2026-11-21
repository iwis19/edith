using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    /// Runs ScrapbookClient against a backend on this machine that answers as the test says.
    public class ScrapbookClientTests
    {
        const string Profile = "11111111-2222-3333-4444-555555555555";
        static readonly byte[] Jpeg = { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9 };

        sealed class Received
        {
            public string Method, PathAndQuery, Token, Profile;
        }

        sealed class FakeBackend : IDisposable
        {
            readonly HttpListener listener = new HttpListener();
            public readonly ConcurrentQueue<Received> Requests = new ConcurrentQueue<Received>();
            public readonly string Url;

            public FakeBackend(Action<HttpListenerResponse> respond)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Url = $"http://127.0.0.1:{port}";
                listener.Prefixes.Add(Url + "/");
                listener.Start();
                new Thread(() =>
                {
                    while (listener.IsListening)
                    {
                        HttpListenerContext context;
                        try
                        {
                            context = listener.GetContext();
                        }
                        catch (Exception)
                        {
                            return;
                        }
                        Requests.Enqueue(new Received
                        {
                            Method = context.Request.HttpMethod,
                            PathAndQuery = context.Request.Url.PathAndQuery,
                            Token = context.Request.Headers["X-Edith-Token"],
                            Profile = context.Request.Headers["X-Edith-Profile"],
                        });
                        new Thread(() =>
                        {
                            try
                            {
                                respond(context.Response);
                                context.Response.Close();
                            }
                            catch (Exception)
                            {
                            }
                        }).Start();
                    }
                }) { IsBackground = true }.Start();
            }

            public void Dispose() => listener.Abort();
        }

        static void Write(HttpListenerResponse response, byte[] body, string type)
        {
            response.ContentType = type;
            response.ContentLength64 = body.Length;
            response.OutputStream.Write(body, 0, body.Length);
        }

        static void Json(HttpListenerResponse response, string json) => Write(response, Encoding.UTF8.GetBytes(json), "application/json");

        // Off the main thread, so nothing waits on Unity's synchronisation context.
        static T Wait<T>(Func<Task<T>> call) => Task.Run(call).GetAwaiter().GetResult();

        [Test]
        public void The_list_asks_for_memories_with_the_token_and_the_profile()
        {
            using var backend = new FakeBackend(response => Json(response, "{\"memories\":[]}"));
            Wait(() => new ScrapbookClient(backend.Url, "secret", Profile).List(CancellationToken.None));
            Assert.IsTrue(backend.Requests.TryPeek(out var request));
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual($"/memories?limit={ScrapbookClient.Limit}", request.PathAndQuery);
            Assert.AreEqual("secret", request.Token);
            Assert.AreEqual(Profile, request.Profile);
        }

        [Test]
        public void A_build_without_a_token_sends_no_token_header()
        {
            using var backend = new FakeBackend(response => Json(response, "{\"memories\":[]}"));
            Wait(() => new ScrapbookClient(backend.Url, "", Profile).List(CancellationToken.None));
            Assert.IsTrue(backend.Requests.TryPeek(out var request));
            Assert.IsNull(request.Token);
        }

        [Test]
        public void The_list_comes_back_newest_first_as_the_backend_sent_it()
        {
            using var backend = new FakeBackend(response => Json(response,
                "{\"memories\":[{\"id\":5,\"name\":\"Kettle\",\"fact\":\"Hot.\",\"rarity\":\"epic\",\"place\":\"Home\",\"date\":\"2026-11-21\"}," +
                "{\"id\":2,\"name\":\"Fern\",\"date\":\"2026-11-20\"}]}"));
            var memories = Wait(() => new ScrapbookClient(backend.Url, "secret", Profile).List(CancellationToken.None));
            Assert.AreEqual(2, memories.Count);
            Assert.AreEqual("Kettle", memories[0].Name);
            Assert.AreEqual(2, memories[1].Id);
        }

        [Test]
        public void An_image_is_the_bytes_of_that_photo()
        {
            using var backend = new FakeBackend(response => Write(response, Jpeg, "image/jpeg"));
            var bytes = Wait(() => new ScrapbookClient(backend.Url, "secret", Profile).Image(7, CancellationToken.None));
            CollectionAssert.AreEqual(Jpeg, bytes);
            Assert.IsTrue(backend.Requests.TryPeek(out var request));
            Assert.AreEqual("/memories/7/image", request.PathAndQuery);
            Assert.AreEqual("secret", request.Token);
            Assert.AreEqual(Profile, request.Profile);
        }

        [Test]
        public void A_refusal_throws()
        {
            using var backend = new FakeBackend(response => response.StatusCode = 401);
            var client = new ScrapbookClient(backend.Url, "wrong", Profile);
            Assert.Throws<HttpRequestException>(() => Wait(() => client.List(CancellationToken.None)));
            Assert.Throws<HttpRequestException>(() => Wait(() => client.Image(7, CancellationToken.None)));
        }

        [Test]
        public void An_answer_that_is_not_a_memory_list_throws()
        {
            using var backend = new FakeBackend(response => Json(response, "{\"detail\":\"nope\"}"));
            Assert.Throws<FormatException>(() => Wait(() => new ScrapbookClient(backend.Url, "", Profile).List(CancellationToken.None)));
        }

        [Test]
        public void A_backend_that_is_not_there_throws()
        {
            string url;
            using (var backend = new FakeBackend(_ => { })) url = backend.Url;
            Assert.Throws<HttpRequestException>(() => Wait(() => new ScrapbookClient(url, "", Profile).List(CancellationToken.None)));
        }

        [Test]
        public void A_cancelled_call_stops()
        {
            using var gate = new ManualResetEventSlim();
            using var backend = new FakeBackend(response =>
            {
                gate.Wait(5000);
                Json(response, "{\"memories\":[]}");
            });
            using var cancel = new CancellationTokenSource(200);
            Assert.Catch<OperationCanceledException>(() => Wait(() => new ScrapbookClient(backend.Url, "", Profile).List(cancel.Token)));
            gate.Set();
        }
    }
}
