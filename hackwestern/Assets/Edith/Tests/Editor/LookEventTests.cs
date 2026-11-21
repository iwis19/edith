using Edith.Look;
using NUnit.Framework;

namespace Edith.Tests
{
    public class LookEventTests
    {
        static LookEvent Parse(string line)
        {
            Assert.IsTrue(LookEvent.TryParse(line, out var e), line);
            return e;
        }

        [Test]
        public void An_accepted_line_carries_the_look_ref()
        {
            var e = Parse("{\"type\":\"accepted\",\"look_id\":3,\"look_ref\":\"abc123\",\"t_ms\":5}");
            Assert.AreEqual(LookEventKind.Accepted, e.Kind);
            Assert.AreEqual("abc123", e.LookRef);
            Assert.IsFalse(e.Ends);
        }

        [Test]
        public void A_final_name_has_its_confidence_and_is_not_pending()
        {
            var e = Parse("{\"type\":\"name\",\"name\":\"Red mug\",\"confidence\":0.9,\"pending\":false}");
            Assert.AreEqual(LookEventKind.Name, e.Kind);
            Assert.AreEqual("Red mug", e.Name);
            Assert.AreEqual(0.9f, e.Confidence, 1e-4f);
            Assert.IsFalse(e.Pending);
        }

        [Test]
        public void A_pending_name_is_marked_pending()
        {
            var e = Parse("{\"type\":\"name\",\"name\":\"Red m\",\"confidence\":0,\"pending\":true}");
            Assert.AreEqual("Red m", e.Name);
            Assert.IsTrue(e.Pending);
        }

        [Test]
        public void A_fact_line_gives_its_text_and_ignores_the_sources()
        {
            var e = Parse("{\"type\":\"fact\",\"text\":\"Mugs are old.\",\"sources\":[{\"title\":\"T\",\"url\":\"https://x\"}]}");
            Assert.AreEqual(LookEventKind.Fact, e.Kind);
            Assert.AreEqual("Mugs are old.", e.Text);
        }

        [TestCase("common", Rarity.Common)]
        [TestCase("uncommon", Rarity.Uncommon)]
        [TestCase("rare", Rarity.Rare)]
        [TestCase("epic", Rarity.Epic)]
        [TestCase("legendary", Rarity.Legendary)]
        [TestCase("mythic", Rarity.Mythic)]
        public void A_rarity_line_gives_its_tier(string tier, Rarity expected)
        {
            var e = Parse("{\"type\":\"rarity\",\"tier\":\"" + tier + "\"}");
            Assert.AreEqual(LookEventKind.Rarity, e.Kind);
            Assert.AreEqual(expected, e.Tier);
        }

        [Test]
        public void Done_and_error_lines_end_the_stream()
        {
            var done = Parse("{\"type\":\"done\",\"t_ms\":900}");
            Assert.AreEqual(LookEventKind.Done, done.Kind);
            Assert.IsTrue(done.Ends);

            var error = Parse("{\"type\":\"error\",\"code\":\"timeout\",\"message\":\"Too slow.\"}");
            Assert.AreEqual(LookEventKind.Error, error.Kind);
            Assert.AreEqual("timeout", error.Code);
            Assert.AreEqual("Too slow.", error.Message);
            Assert.IsTrue(error.Ends);
        }

        [TestCase("{\"type\":\"sparkle\",\"x\":1}")]
        [TestCase("{\"type\":\"rarity\",\"tier\":\"shiny\"}")]
        [TestCase("{\"no_type\":true}")]
        [TestCase("not json at all")]
        [TestCase("{\"type\":\"name\",")]
        [TestCase("")]
        [TestCase("null")]
        public void A_line_it_cannot_use_yields_no_event_and_never_throws(string line)
        {
            Assert.IsFalse(LookEvent.TryParse(line, out _));
        }
    }
}
