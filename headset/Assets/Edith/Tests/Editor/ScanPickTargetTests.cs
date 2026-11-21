using Edith.Scan;
using NUnit.Framework;

namespace Edith.Tests
{
    public class ScanPickTargetTests
    {
        const string Mug = "\"name\":\"mug\",\"hook\":\"Sip.\",\"point\":[0.5,0.5],\"box\":[0.4,0.4,0.6,0.6]";
        const string Lamp = "\"name\":\"lamp\",\"hook\":\"Switch.\",\"point\":[0.2,0.2],\"box\":[0.1,0.1,0.3,0.3]";

        [Test]
        public void A_pick_has_the_hunt_target_it_fits()
        {
            var picks = ScanReply.Parse("{\"picks\":[{" + Mug + ",\"target\":3},{" + Lamp + ",\"target\":0}]}");

            Assert.AreEqual(3, picks[0].Target);
            Assert.AreEqual(0, picks[1].Target, "target 0 is a target, not an absent one");
        }

        [Test]
        public void A_pick_with_a_null_target_has_none()
        {
            var picks = ScanReply.Parse("{\"picks\":[{" + Mug + ",\"target\":null},{" + Lamp + ",\"target\": null }]}");

            Assert.AreEqual(2, picks.Count);
            Assert.IsNull(picks[0].Target);
            Assert.IsNull(picks[1].Target);
        }

        [Test]
        public void A_pick_from_a_backend_that_sends_no_target_has_none()
        {
            var picks = ScanReply.Parse("{\"picks\":[{" + Mug + "},{" + Lamp + "}]}");

            Assert.AreEqual(2, picks.Count);
            Assert.IsNull(picks[0].Target);
            Assert.IsNull(picks[1].Target);
        }

        [Test]
        public void A_target_does_not_change_which_picks_are_kept()
        {
            var picks = ScanReply.Parse("{\"picks\":[{\"name\":\"bad\",\"point\":[0.5],\"box\":[0,0,1,1],\"target\":1},{" + Mug + ",\"target\":2}]}");

            Assert.AreEqual(1, picks.Count);
            Assert.AreEqual("mug", picks[0].Name);
            Assert.AreEqual(2, picks[0].Target);
        }
    }
}
