using System.Collections.Generic;
using Edith.Bounty;
using NUnit.Framework;

namespace Edith.Tests
{
    public class HuntTrackerTests
    {
        float now;
        HuntTracker tracker;
        readonly List<string> events = new List<string>();

        [SetUp]
        public void SetUp()
        {
            now = 1000f;
            events.Clear();
            tracker = new HuntTracker(() => now);
            tracker.Started += () => events.Add("started");
            tracker.TargetFound += index => events.Add("found " + index);
            tracker.Ticked += seconds => events.Add("tick " + seconds);
            tracker.Ended += reason => events.Add("ended " + reason);
        }

        static HuntState Hunt(string huntRef = "h1", HuntStatus status = HuntStatus.Active, float remaining = 300f, params int[] found)
        {
            var targets = new List<HuntTarget>();
            for (var i = 0; i < 5; i++) targets.Add(new HuntTarget(i, "target " + i, null, System.Array.IndexOf(found, i) >= 0));
            return new HuntState(huntRef, status, remaining, targets);
        }

        void Advance(float seconds)
        {
            now += seconds;
            tracker.Update();
        }

        [Test]
        public void With_no_hunt_the_tracker_is_none()
        {
            Assert.AreEqual(HuntPhase.None, tracker.Phase);
            Assert.AreEqual(0f, tracker.Remaining);
            tracker.Update();
            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void An_active_hunt_counts_down_from_the_seconds_the_backend_gave_on_the_local_clock()
        {
            tracker.Apply(Hunt(remaining: 300f));
            Assert.AreEqual(HuntPhase.Active, tracker.Phase);
            Assert.AreEqual(300f, tracker.Remaining, 1e-3f);

            Advance(42f);
            Assert.AreEqual(258f, tracker.Remaining, 1e-3f);
            CollectionAssert.AreEqual(new[] { "started" }, events);
        }

        [Test]
        public void A_hunt_read_mid_way_counts_from_its_own_remaining_time()
        {
            tracker.Apply(Hunt(remaining: 120f));
            Advance(20f);

            Assert.AreEqual(100f, tracker.Remaining, 1e-3f);
        }

        [Test]
        public void When_the_time_runs_out_the_hunt_ends_as_a_timeout_once()
        {
            tracker.Apply(Hunt(remaining: 5f, found: new[] { 0, 2 }));
            Advance(4.9f);
            Assert.AreEqual(HuntPhase.Active, tracker.Phase);

            Advance(0.2f);
            Assert.AreEqual(HuntPhase.Ended, tracker.Phase);
            Assert.AreEqual(HuntEnd.Timeout, tracker.Ending);
            Assert.AreEqual(2, tracker.Found, "the result keeps what was found");
            Advance(10f);
            Assert.AreEqual(1, events.FindAll(e => e == "ended Timeout").Count);
            Assert.AreEqual(0f, tracker.Remaining);
        }

        [Test]
        public void The_time_since_the_end_is_counted_on_the_same_clock()
        {
            tracker.Apply(Hunt(remaining: 1f));
            Advance(1.5f);
            Advance(3f);

            Assert.AreEqual(3f, tracker.SinceEnded, 1e-3f);
        }

        [Test]
        public void Ticks_fire_at_thirty_twenty_and_ten_seconds_left_and_not_before()
        {
            tracker.Apply(Hunt(remaining: 100f));
            Advance(69f);
            CollectionAssert.AreEqual(new[] { "started" }, events, "31 s left");

            Advance(1f);
            CollectionAssert.AreEqual(new[] { "started", "tick 30" }, events);
            Advance(9.5f);
            CollectionAssert.AreEqual(new[] { "started", "tick 30" }, events, "20.5 s left");
            Advance(0.5f);
            Advance(10f);
            CollectionAssert.AreEqual(new[] { "started", "tick 30", "tick 20", "tick 10" }, events);
            Advance(10f);
            CollectionAssert.AreEqual(new[] { "started", "tick 30", "tick 20", "tick 10", "ended Timeout" }, events);
        }

        [Test]
        public void A_hunt_read_with_under_thirty_seconds_left_does_not_tick_the_marks_it_is_already_past()
        {
            tracker.Apply(Hunt(remaining: 25f));
            Advance(6f);
            CollectionAssert.DoesNotContain(events, "tick 30");
            CollectionAssert.Contains(events, "tick 20");

            Advance(10f);
            CollectionAssert.Contains(events, "tick 10");
        }

        [Test]
        public void Reading_the_same_hunt_again_does_not_repeat_a_tick()
        {
            tracker.Apply(Hunt(remaining: 40f));
            Advance(11f);
            CollectionAssert.Contains(events, "tick 30");

            tracker.Apply(Hunt(remaining: 30.4f));
            Advance(0.1f);
            Assert.AreEqual(1, events.FindAll(e => e == "tick 30").Count);
        }

        [Test]
        public void A_find_read_from_the_backend_raises_found_once()
        {
            tracker.Apply(Hunt(remaining: 200f));
            tracker.Apply(Hunt(remaining: 190f, found: new[] { 3 }));
            tracker.Apply(Hunt(remaining: 189f, found: new[] { 3 }));

            CollectionAssert.AreEqual(new[] { "started", "found 3" }, events);
        }

        [Test]
        public void A_look_that_ticks_a_target_marks_it_at_once_and_the_backends_word_then_adds_nothing()
        {
            tracker.Apply(Hunt(remaining: 200f));
            tracker.MarkFound(2, completes: false);
            Assert.AreEqual(1, tracker.Found);
            tracker.Apply(Hunt(remaining: 195f, found: new[] { 2 }));

            CollectionAssert.AreEqual(new[] { "started", "found 2" }, events);
            tracker.MarkFound(2, completes: false);
            Assert.AreEqual(1, events.FindAll(e => e == "found 2").Count, "a second tick of one target is ignored");
        }

        [Test]
        public void The_find_that_completes_the_hunt_ends_it_as_found_all_without_a_found_event()
        {
            tracker.Apply(Hunt(remaining: 100f, found: new[] { 0, 1, 2, 3 }));
            tracker.MarkFound(4, completes: true);

            Assert.AreEqual(HuntPhase.Ended, tracker.Phase);
            Assert.AreEqual(HuntEnd.FoundAll, tracker.Ending);
            Assert.AreEqual(5, tracker.Found);
            CollectionAssert.AreEqual(new[] { "started", "ended FoundAll" }, events);
        }

        [Test]
        public void A_tick_for_a_hunt_that_is_not_active_is_ignored()
        {
            tracker.MarkFound(0, completes: false);
            tracker.Apply(Hunt(status: HuntStatus.Stopped, remaining: 0f));
            tracker.MarkFound(0, completes: false);

            Assert.AreEqual(0, tracker.Found);
        }

        [Test]
        public void Stopping_ends_the_hunt_as_stopped()
        {
            tracker.Apply(Hunt(remaining: 200f));
            tracker.Apply(Hunt(status: HuntStatus.Stopped, remaining: 0f, found: new[] { 1 }));

            Assert.AreEqual(HuntEnd.Stopped, tracker.Ending);
            Assert.AreEqual(1, tracker.Found);
            CollectionAssert.AreEqual(new[] { "started", "ended Stopped" }, events);
        }

        [Test]
        public void The_backend_saying_there_is_no_hunt_ends_one_that_was_running_and_changes_nothing_otherwise()
        {
            tracker.NoHunt();
            Assert.AreEqual(HuntPhase.None, tracker.Phase);

            tracker.Apply(Hunt(remaining: 200f));
            tracker.NoHunt();
            Assert.AreEqual(HuntEnd.Stopped, tracker.Ending);
        }

        [Test]
        public void A_new_hunt_replaces_an_ended_one()
        {
            tracker.Apply(Hunt("h1", HuntStatus.Timeout, 0f));
            tracker.Apply(Hunt("h2", remaining: 300f));

            Assert.AreEqual(HuntPhase.Active, tracker.Phase);
            Assert.AreEqual("h2", tracker.HuntRef);
            Assert.AreEqual(0, tracker.Found);
            CollectionAssert.AreEqual(new[] { "ended Timeout", "started" }, events);
        }

        [Test]
        public void The_same_ended_hunt_read_again_does_not_end_twice()
        {
            tracker.Apply(Hunt(remaining: 10f));
            tracker.Apply(Hunt(status: HuntStatus.Stopped, remaining: 0f));
            tracker.Apply(Hunt(status: HuntStatus.Stopped, remaining: 0f));

            Assert.AreEqual(1, events.FindAll(e => e.StartsWith("ended")).Count);
        }

        [Test]
        public void A_target_is_found_by_its_index_for_its_text()
        {
            tracker.Apply(Hunt());

            Assert.AreEqual("target 3", tracker.TextOf(3));
            Assert.IsNull(tracker.TextOf(9));
        }
    }
}
