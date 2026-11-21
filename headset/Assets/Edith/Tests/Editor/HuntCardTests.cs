using System.Collections.Generic;
using Edith.Bounty;
using NUnit.Framework;

namespace Edith.Tests
{
    public class HuntCardTests
    {
        static HuntTracker Tracker(HuntStatus status, float remaining, params int[] found)
        {
            var tracker = new HuntTracker(() => 0f);
            var targets = new List<HuntTarget>();
            for (var i = 0; i < 5; i++) targets.Add(new HuntTarget(i, "target " + i, null, System.Array.IndexOf(found, i) >= 0));
            tracker.Apply(new HuntState("h", status, remaining, targets));
            return tracker;
        }

        [TestCase(300f, "5:00")]
        [TestCase(299.2f, "5:00")]
        [TestCase(61f, "1:01")]
        [TestCase(59.5f, "1:00")]
        [TestCase(9f, "0:09")]
        [TestCase(0.2f, "0:01")]
        [TestCase(0f, "0:00")]
        [TestCase(-3f, "0:00")]
        public void The_clock_reads_minutes_and_seconds_rounding_a_part_second_up(float seconds, string expected)
        {
            Assert.AreEqual(expected, HuntCard.Clock(seconds));
        }

        [Test]
        public void A_running_hunt_says_how_many_are_found_of_all()
        {
            Assert.AreEqual("BOUNTY  2 of 5", HuntCard.Header(Tracker(HuntStatus.Active, 200f, 0, 4)));
        }

        [Test]
        public void An_ended_hunt_says_its_result()
        {
            Assert.AreEqual("All 5 found!", HuntCard.Header(Tracker(HuntStatus.FoundAll, 0f, 0, 1, 2, 3, 4)));
            Assert.AreEqual("Time's up, found 3 of 5", HuntCard.Header(Tracker(HuntStatus.Timeout, 0f, 0, 1, 2)));
            Assert.AreEqual("Hunt stopped, found 0 of 5", HuntCard.Header(Tracker(HuntStatus.Stopped, 0f)));
        }

        [Test]
        public void Each_target_has_a_line_and_a_found_one_is_ticked()
        {
            var lines = HuntCard.Lines(Tracker(HuntStatus.Active, 200f, 1)).Split('\n');

            Assert.AreEqual(5, lines.Length);
            Assert.AreEqual("- target 0", lines[0]);
            StringAssert.Contains("✓ target 1", lines[1]);
            Assert.AreEqual("- target 4", lines[4]);
        }
    }
}
