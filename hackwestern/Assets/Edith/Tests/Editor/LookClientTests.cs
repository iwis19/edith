using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Edith.Look;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Edith.Tests
{
    /// Runs LookClient against a backend on this machine that answers as the test says.
    public class LookClientTests
    {
        static readonly DateTime Captured = new DateTime(2026, 11, 21, 15, 4, 5, DateTimeKind.Utc);
        static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xD9 };
        static readonly LookTimeouts Quick = new LookTimeouts(0.5f, 0.5f, 5f);

        sealed class Received
        {
            public string Path, Token, Profile, Body;
        }

        /// Answers each POST with `respond`, which gets the 1-based number of the request.
        sealed class FakeBackend : IDisposable
        {
            readonly HttpListener listener = new HttpListener();
            public readonly ConcurrentQueue<Received> Requests = new ConcurrentQueue<Received>();
            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim();
            public readonly string Url;

            public FakeBackend(Action<HttpListenerResponse, int> respond)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Url = $"http://127.0.0.1:{port}";
                listener.Prefixes.Add(Url + "/");
                listener.Start();
                var count = 0;
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
                        var number = Interlocked.Increment(ref count);
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.GetEncoding("iso-8859-1"));
                        Requests.Enqueue(new Received
                        {
                            Path = context.Request.Url.AbsolutePath,
                            Token = context.Request.Headers["X-Edith-Token"],
                            Profile = context.Request.Headers["X-Edith-Profile"],
                            Body = reader.ReadToEnd(),
                        });
                        new Thread(() =>
                        {
                            try
                            {
                                respond(context.Response, number);
                            }
                            catch (Exception)
                            {
                            }
                            try
                            {
                                context.Response.Close();
                            }
                            catch (Exception)
                            {
                            }
                        }).Start();
                    }
                }) { IsBackground = true }.Start();
            }

            public void Dispose()
            {
                Gate.Set();
                listener.Abort();
            }
        }

        static void Line(HttpListenerResponse response, string json)
        {
            if (!response.SendChunked) response.SendChunked = true;
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
        }

        static LookClient Send(FakeBackend backend, LookTimeouts? timeouts = null) =>
            LookClient.Send(backend.Url, "secret", "11111111-2222-3333-4444-555555555555", 42, Jpeg, Captured, timeouts ?? Quick);

        static List<LookEvent> Collect(LookClient client, float seconds = 10f)
        {
            var events = new List<LookEvent>();
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                while (client.TryNext(out var e))
                {
                    events.Add(e);
                    if (e.Ends) return events;
                }
                Thread.Sleep(5);
            }
            return events;
        }

        [Test]
        public void A_look_streams_its_events_and_ends_with_done()
        {
            using var backend = new FakeBackend((response, _) =>
            {
                Line(response, "{\"type\":\"accepted\",\"look_id\":42,\"look_ref\":\"r\"}");
                Line(response, "{\"type\":\"name\",\"name\":\"Red m\",\"confidence\":0,\"pending\":true}");
                Line(response, "{\"type\":\"name\",\"name\":\"Red mug\",\"confidence\":0.9}");
                Line(response, "{\"type\":\"fact\",\"text\":\"Old.\",\"sources\":[]}");
                Line(response, "{\"type\":\"rarity\",\"tier\":\"rare\"}");
                Line(response, "{\"type\":\"done\"}");
            });
            var events = Collect(Send(backend));
            CollectionAssert.AreEqual(
                new[] { LookEventKind.Accepted, LookEventKind.Name, LookEventKind.Name, LookEventKind.Fact, LookEventKind.Rarity, LookEventKind.Done },
                events.ConvertAll(e => e.Kind));
            Assert.AreEqual(1, backend.Requests.Count);
        }

        [Test]
        public void The_request_carries_the_headers_the_photo_and_the_meta()
        {
            using var backend = new FakeBackend((response, _) => Line(response, "{\"type\":\"done\"}"));
            Collect(Send(backend));
            Assert.IsTrue(backend.Requests.TryPeek(out var request));
            Assert.AreEqual("secret", request.Token);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", request.Profile);
            StringAssert.Contains("name=image", request.Body.Replace("\"", ""));
            StringAssert.Contains("image/jpeg", request.Body);
            StringAssert.Contains("\"look_id\":42", request.Body);
            StringAssert.Contains("\"source\":\"pinch\"", request.Body);
            StringAssert.Contains("\"captured_at\":\"2026-11-21T15:04:05.000Z\"", request.Body);
        }

        [Test]
        public void Events_arrive_while_the_response_is_still_open()
        {
            var gate = new ManualResetEventSlim();
            using var open = new FakeBackend((response, _) =>
            {
                Line(response, "{\"type\":\"accepted\",\"look_id\":42,\"look_ref\":\"r\"}");
                gate.Wait(5000);
                Line(response, "{\"type\":\"done\"}");
            });
            var client = Send(open, new LookTimeouts(2f, 2f, 10f));
            var first = new List<LookEvent>();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (first.Count == 0 && DateTime.UtcNow < deadline)
            {
                if (client.TryNext(out var e)) first.Add(e);
                else Thread.Sleep(5);
            }
            Assert.AreEqual(LookEventKind.Accepted, first[0].Kind, "the first event came before the response ended");
            gate.Set();
            Assert.AreEqual(LookEventKind.Done, Collect(client)[0].Kind);
        }

        [Test]
        public void A_server_error_is_retried_once_then_reported_by_its_status()
        {
            using var backend = new FakeBackend((response, _) => response.StatusCode = 503);
            var events = Collect(Send(backend));
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(LookEventKind.Error, events[0].Kind);
            Assert.AreEqual("http_503", events[0].Code);
            Assert.IsNotEmpty(events[0].Message);
            Assert.AreEqual(2, backend.Requests.Count);
        }

        [Test]
        public void A_second_try_that_works_shows_no_error()
        {
            using var backend = new FakeBackend((response, number) =>
            {
                if (number == 1) response.StatusCode = 502;
                else Line(response, "{\"type\":\"done\"}");
            });
            var events = Collect(Send(backend));
            CollectionAssert.AreEqual(new[] { LookEventKind.Done }, events.ConvertAll(e => e.Kind));
            Assert.AreEqual(2, backend.Requests.Count);
        }

        [Test]
        public void A_refusal_is_not_retried()
        {
            using var backend = new FakeBackend((response, _) => response.StatusCode = 422);
            var events = Collect(Send(backend));
            Assert.AreEqual("http_422", events[0].Code);
            Assert.AreEqual(1, backend.Requests.Count);
        }

        [Test]
        public void A_backend_that_never_answers_times_out_after_one_retry()
        {
            using var backend = new FakeBackend((response, _) => Thread.Sleep(3000));
            var events = Collect(Send(backend));
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("timeout", events[0].Code);
            Assert.AreEqual(2, backend.Requests.Count);
        }

        [Test]
        public void A_stream_that_goes_quiet_after_its_headers_times_out()
        {
            using var backend = new FakeBackend((response, _) =>
            {
                Line(response, "");
                Thread.Sleep(3000);
            });
            var events = Collect(Send(backend));
            Assert.AreEqual("timeout", events[0].Code);
        }

        [Test]
        public void A_stream_that_ends_without_done_is_one_error_and_not_retried()
        {
            using var backend = new FakeBackend((response, _) =>
                Line(response, "{\"type\":\"name\",\"name\":\"Red mug\",\"confidence\":0.9}"));
            var events = Collect(Send(backend));
            CollectionAssert.AreEqual(new[] { LookEventKind.Name, LookEventKind.Error }, events.ConvertAll(e => e.Kind));
            Assert.AreEqual("stream_ended", events[1].Code);
            Assert.AreEqual(1, backend.Requests.Count);
        }

        [Test]
        public void The_backends_own_error_ends_the_look_and_what_follows_is_dropped()
        {
            using var backend = new FakeBackend((response, _) =>
            {
                Line(response, "{\"type\":\"error\",\"code\":\"model_failed\",\"message\":\"Could not tell.\"}");
                Line(response, "{\"type\":\"done\"}");
            });
            var client = Send(backend);
            var events = Collect(client);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("model_failed", events[0].Code);
            Thread.Sleep(100);
            Assert.IsFalse(client.TryNext(out _));
        }

        [Test]
        public void Lines_that_do_not_parse_are_skipped_and_the_stream_goes_on()
        {
            using var backend = new FakeBackend((response, _) =>
            {
                Line(response, "garbage");
                Line(response, "{\"type\":\"sparkle\"}");
                Line(response, "{\"type\":\"done\"}");
            });
            CollectionAssert.AreEqual(new[] { LookEventKind.Done }, Collect(Send(backend)).ConvertAll(e => e.Kind));
        }

        [Test]
        public void An_unreachable_backend_is_a_network_error()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var client = LookClient.Send($"http://127.0.0.1:{port}", "", "p", 1, Jpeg, Captured, new LookTimeouts(5f, 5f, 20f));
            var events = Collect(client, 20f);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("network", events[0].Code);
        }

        [Test]
        public void Cancelling_closes_the_connection_and_drops_everything_that_follows()
        {
            var gate = new ManualResetEventSlim();
            var writeFailed = new ManualResetEventSlim();
            using var backend = new FakeBackend((response, _) =>
            {
                Line(response, "{\"type\":\"accepted\",\"look_id\":42,\"look_ref\":\"r\"}");
                gate.Wait(5000);
                try
                {
                    for (var i = 0; i < 200; i++)
                    {
                        Line(response, "{\"type\":\"rarity\",\"tier\":\"rare\"}");
                        Thread.Sleep(20);
                    }
                }
                catch (Exception)
                {
                    writeFailed.Set();
                }
            });
            var client = Send(backend, new LookTimeouts(2f, 2f, 10f));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (backend.Requests.IsEmpty && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Thread.Sleep(200);
            client.Cancel();
            gate.Set();
            Assert.IsTrue(writeFailed.Wait(5000), "the server's writes fail once the client has hung up");
            Assert.IsFalse(client.TryNext(out _));
        }

        [Test]
        public void A_collect_carries_the_headers_the_photo_and_the_look_ref()
        {
            using var backend = new FakeBackend((response, _) => Line(response, "{\"memory_id\":7}"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[EDITH collect\] look abc123 saved: .*memory_id"));
            CollectClient.Send(backend.Url, "secret", "11111111-2222-3333-4444-555555555555", "abc123", Jpeg, Captured).Wait(10000);
            Assert.IsTrue(backend.Requests.TryPeek(out var request));
            Assert.AreEqual("/collect", request.Path);
            Assert.AreEqual("secret", request.Token);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", request.Profile);
            StringAssert.Contains("name=image", request.Body.Replace("\"", ""));
            StringAssert.Contains("image/jpeg", request.Body);
            StringAssert.Contains("name=meta", request.Body.Replace("\"", ""));
            StringAssert.Contains("{\"look_ref\":\"abc123\",\"collected_at\":\"2026-11-21T15:04:05.000Z\"}", request.Body);
        }

        [Test]
        public void An_unknown_look_is_logged_and_not_retried()
        {
            using var backend = new FakeBackend((response, _) => response.StatusCode = 404);
            LogAssert.Expect(LogType.Log, new Regex(@"\[EDITH collect\] look abc123 failed with http_404"));
            CollectClient.Send(backend.Url, "", "p", "abc123", Jpeg, Captured).Wait(10000);
            Thread.Sleep(200);
            Assert.AreEqual(1, backend.Requests.Count);
        }

        [Test]
        public void A_look_is_collected_only_once_the_backend_accepted_it_and_it_did_not_fail()
        {
            var card = new LookCard();
            StringAssert.Contains("never accepted", CollectClient.SkipReason(card));
            card.Apply(LookEvent.Accepted("r"));
            Assert.IsNull(CollectClient.SkipReason(card));
            card.Apply(LookEvent.Done());
            Assert.IsNull(CollectClient.SkipReason(card));
            var failed = new LookCard();
            failed.Apply(LookEvent.Accepted("r"));
            failed.Apply(LookEvent.Failure("model_failed", "x"));
            StringAssert.Contains("failed", CollectClient.SkipReason(failed));
        }
    }
}
