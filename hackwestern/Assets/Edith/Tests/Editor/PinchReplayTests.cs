using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Edith.Hands;
using Edith.Pinch;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    /// Replays right-hand sessions recorded on a Quest 3S through the pinch tracker. Times are seconds on each
    /// recording's own clock.
    public class PinchReplayTests
    {
        const string Dropouts = "right-hand-dropouts.jsonl.gz";
        const string Clean = "right-hand-clean.jsonl.gz";
        /// Six quick drags of about 30 cm each, read through OVRHand's pinch flag.
        const string FastDrags = "right-hand-fast-drags.jsonl.gz";

        // Mid-drag dropouts: the last pinching frame and the frame the pinch came back on. The pinch flag dropped for
        // 194 and 222 ms, and at 21.2 s tracking also reported the fingertips 5.8 cm apart before they closed again.
        static readonly (float at, float back)[] MidDragDropouts = { (21.216f, 21.438f), (72.772f, 72.966f) };

        static List<HandFrame> Load(string fixture)
        {
            var path = Path.Combine(Application.dataPath, "Edith", "Tests", "Editor", "Fixtures", fixture);
            var frames = new List<HandFrame>();
            using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
            string line;
            while ((line = reader.ReadLine()) != null)
                if (line.Length > 0) frames.Add(JsonUtility.FromJson<HandFrame>(line));
            return frames;
        }

        static List<(float time, PinchEvent pinch)> Replay(string fixture)
        {
            var tracker = new PinchTracker(new PinchTuning());
            var events = new List<(float, PinchEvent)>();
            foreach (var frame in Load(fixture))
            {
                var reading = tracker.Step(frame);
                if (reading.Event != PinchEvent.None) events.Add((frame.Time, reading.Event));
            }
            TestContext.WriteLine($"{fixture}: " + string.Join(", ", events.Select(e => $"{e.Item1:F2} {e.Item2}")));
            return events;
        }

        /// Releases that stand: every Completed not undone by a Resumed.
        static List<float> Looks(List<(float time, PinchEvent pinch)> events)
        {
            var looks = new List<float>();
            for (var i = 0; i < events.Count; i++)
                if (events[i].pinch == PinchEvent.Completed && !(i + 1 < events.Count && events[i + 1].pinch == PinchEvent.Resumed))
                    looks.Add(events[i].time);
            return looks;
        }

        static bool Between(float t, float from, float to) => t > from && t <= to;

        [Test]
        public void Mid_drag_dropouts_continue_the_drag_to_one_Look()
        {
            var events = Replay(Dropouts);
            var looks = Looks(events);
            foreach (var (at, back) in MidDragDropouts)
            {
                Assert.IsFalse(looks.Any(t => Between(t, at, back)), $"a Look stood before the pinch came back at {back:F2} s");
                Assert.IsFalse(events.Any(e => (e.pinch == PinchEvent.Tap || e.pinch == PinchEvent.Cancelled) && Between(e.time, at, back + 0.5f)),
                    $"the drag broke at the dropout at {at:F2} s");
                Assert.IsTrue(looks.Any(t => Between(t, back, back + 1f)), $"the drag resumed at {back:F2} s should end in a Look");
            }
        }

        [Test]
        public void A_clean_session_resumes_nothing_and_keeps_its_Looks()
        {
            var events = Replay(Clean);
            Assert.IsFalse(events.Any(e => e.pinch == PinchEvent.Resumed), "no drag in this session had a dropout");
            Assert.Greater(Looks(events).Count, 5);
        }

        [Test]
        public void Each_fast_drag_ends_in_one_Look()
        {
            var events = Replay(FastDrags);
            Assert.AreEqual(6, Looks(events).Count);
            Assert.IsFalse(events.Any(e => e.pinch == PinchEvent.Resumed || e.pinch == PinchEvent.Cancelled || e.pinch == PinchEvent.Tap));
        }

        [TestCase(Dropouts)]
        [TestCase(Clean)]
        [TestCase(FastDrags)]
        public void Every_release_settles_within_the_rejoin_window(string fixture)
        {
            var tuning = new PinchTuning();
            var events = Replay(fixture);
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].pinch != PinchEvent.Completed) continue;
                Assert.Less(i + 1, events.Count, $"the release at {events[i].time:F2} s never settled");
                var next = events[i + 1];
                Assert.That(next.pinch, Is.EqualTo(PinchEvent.Confirmed).Or.EqualTo(PinchEvent.Resumed), $"after the release at {events[i].time:F2} s");
                Assert.LessOrEqual(next.time - events[i].time, tuning.rejoinSeconds + 0.03f, $"the release at {events[i].time:F2} s took too long to settle");
            }
        }
    }
}
