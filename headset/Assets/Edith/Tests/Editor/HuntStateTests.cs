using Edith.Bounty;
using NUnit.Framework;

namespace Edith.Tests
{
    public class HuntStateTests
    {
        public const string Active =
            "{\"hunt_ref\":\"0123456789abcdef0123456789abcdef\",\"status\":\"active\",\"started_at\":\"2026-11-21T10:00:00Z\"," +
            "\"ends_at\":\"2026-11-21T10:05:00Z\",\"ended_at\":null,\"remaining_s\":287,\"place\":\"Waterloo, Ontario, Canada\",\"found\":1," +
            "\"targets\":[" +
            "{\"index\":3,\"text\":\"something Uncommon or better\",\"tier\":\"uncommon\",\"found\":false,\"found_at\":null,\"look_ref\":null,\"memory_id\":null}," +
            "{\"index\":0,\"text\":\"something red\",\"tier\":null,\"found\":true,\"found_at\":\"2026-11-21T10:00:12Z\",\"look_ref\":\"abc\",\"memory_id\":null}," +
            "{\"index\":1,\"text\":\"a round thing\",\"tier\":null,\"found\":false,\"found_at\":null,\"look_ref\":null,\"memory_id\":null}," +
            "{\"index\":2,\"text\":\"something with text\",\"tier\":null,\"found\":false,\"found_at\":null,\"look_ref\":null,\"memory_id\":null}," +
            "{\"index\":4,\"text\":\"something old\",\"tier\":null,\"found\":false,\"found_at\":null,\"look_ref\":null,\"memory_id\":null}]}";

        [Test]
        public void An_active_hunt_parses_with_its_clock_and_targets_in_order()
        {
            Assert.IsTrue(HuntState.TryParse(Active, out var state));

            Assert.AreEqual("0123456789abcdef0123456789abcdef", state.HuntRef);
            Assert.AreEqual(HuntStatus.Active, state.Status);
            Assert.AreEqual(287f, state.RemainingSeconds);
            Assert.AreEqual(5, state.Targets.Count);
            for (var i = 0; i < 5; i++) Assert.AreEqual(i, state.Targets[i].Index);
            Assert.AreEqual("something red", state.Targets[0].Text);
            Assert.IsTrue(state.Targets[0].Found);
            Assert.AreEqual(1, state.Found);
        }

        [Test]
        public void A_rarity_target_has_its_tier_and_the_others_have_none()
        {
            HuntState.TryParse(Active, out var state);

            Assert.AreEqual("uncommon", state.Targets[3].Tier);
            Assert.IsNull(state.Targets[0].Tier);
        }

        [TestCase("found_all", HuntStatus.FoundAll)]
        [TestCase("stopped", HuntStatus.Stopped)]
        [TestCase("timeout", HuntStatus.Timeout)]
        public void An_ended_hunt_has_its_status_and_no_time_left(string status, HuntStatus expected)
        {
            var json = Active.Replace("\"status\":\"active\"", "\"status\":\"" + status + "\"").Replace("\"remaining_s\":287", "\"remaining_s\":0");
            Assert.IsTrue(HuntState.TryParse(json, out var state));

            Assert.AreEqual(expected, state.Status);
            Assert.AreEqual(0f, state.RemainingSeconds);
        }

        [Test]
        public void Fields_this_build_does_not_know_are_ignored()
        {
            var json = Active.Replace("\"found\":1,", "\"found\":1,\"prize\":{\"coins\":3},");

            Assert.IsTrue(HuntState.TryParse(json, out var state));
            Assert.AreEqual(5, state.Targets.Count);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"error\":{\"code\":\"no_hunt\",\"message\":\"There is no hunt.\"}}")]
        [TestCase("{\"hunt_ref\":\"a\",\"status\":\"paused\",\"targets\":[{\"index\":0,\"text\":\"x\",\"found\":false}]}")]
        [TestCase("{\"hunt_ref\":\"a\",\"status\":\"active\",\"targets\":[]}")]
        public void Anything_else_is_not_a_hunt_and_never_throws(string json)
        {
            Assert.IsFalse(HuntState.TryParse(json, out _));
        }
    }
}
