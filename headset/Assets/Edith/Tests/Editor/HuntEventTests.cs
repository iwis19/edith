using Edith.Look;
using NUnit.Framework;

namespace Edith.Tests
{
    public class HuntEventTests
    {
        [Test]
        public void A_hunt_line_gives_the_target_the_count_and_the_status()
        {
            Assert.IsTrue(LookEvent.TryParse(
                "{\"type\":\"hunt\",\"index\":3,\"text\":\"something red\",\"found\":2,\"total\":5,\"status\":\"active\",\"t_ms\":812}",
                out var e));

            Assert.AreEqual(LookEventKind.Hunt, e.Kind);
            Assert.AreEqual(3, e.HuntIndex);
            Assert.AreEqual("something red", e.Text);
            Assert.AreEqual(2, e.HuntFound);
            Assert.AreEqual(5, e.HuntTotal);
            Assert.AreEqual("active", e.HuntStatus);
            Assert.IsFalse(e.Ends);
        }

        [Test]
        public void A_hunt_line_with_fields_this_build_does_not_know_still_parses()
        {
            Assert.IsTrue(LookEvent.TryParse(
                "{\"type\":\"hunt\",\"index\":0,\"text\":\"a leaf\",\"found\":5,\"total\":5,\"status\":\"found_all\",\"bonus\":{\"points\":10},\"t_ms\":1}",
                out var e));

            Assert.AreEqual(LookEventKind.Hunt, e.Kind);
            Assert.AreEqual(0, e.HuntIndex);
            Assert.AreEqual("found_all", e.HuntStatus);
            Assert.AreEqual(5, e.HuntFound);
        }

        [Test]
        public void A_card_remembers_the_target_its_look_ticked_and_keeps_the_rest()
        {
            var card = new LookCard();
            card.Apply(LookEvent.Named("Red mug"));
            card.Apply(LookEvent.Hunt(1, "something red", 1, 5, "active"));

            Assert.AreEqual("something red", card.HuntText);
            Assert.AreEqual("Red mug", card.Name);
            Assert.AreEqual(LookPhase.Named, card.Phase);
        }

        [Test]
        public void A_card_whose_look_ticked_nothing_has_no_hunt_text()
        {
            var card = new LookCard();
            card.Apply(LookEvent.Named("Red mug"));

            Assert.IsNull(card.HuntText);
        }
    }
}
