using Edith.Look;
using NUnit.Framework;

namespace Edith.Tests
{
    public class LookCardTests
    {
        LookCard card;

        [SetUp]
        public void SetUp() => card = new LookCard();

        [Test]
        public void The_look_ref_is_empty_until_the_look_is_accepted()
        {
            Assert.AreEqual("", card.LookRef);
            card.Apply(LookEvent.Accepted("abc123"));
            Assert.AreEqual("abc123", card.LookRef);
        }

        [Test]
        public void A_new_card_is_looking()
        {
            Assert.AreEqual(LookPhase.Looking, card.Phase);
            Assert.IsFalse(card.Finished);
            Assert.IsNull(card.Tier);
        }

        [Test]
        public void The_happy_path_walks_from_looking_to_done()
        {
            card.Apply(LookEvent.Accepted("ref"));
            Assert.AreEqual(LookPhase.Looking, card.Phase);

            card.Apply(LookEvent.Named("Red m", pending: true));
            Assert.AreEqual(LookPhase.Naming, card.Phase);
            Assert.AreEqual("Red m", card.Name);

            card.Apply(LookEvent.Named("Red mug", 0.9f));
            Assert.AreEqual(LookPhase.Named, card.Phase);
            Assert.AreEqual("Red mug", card.Name);

            card.Apply(LookEvent.Fact("Mugs are old."));
            Assert.IsTrue(card.Typing);
            card.Advance(10f);
            Assert.IsFalse(card.Typing);
            Assert.AreEqual("Mugs are old.", card.Fact.Visible);

            card.Apply(LookEvent.Rated(Rarity.Rare));
            Assert.AreEqual(Rarity.Rare, card.Tier);
            Assert.IsFalse(card.Finished);

            card.Apply(LookEvent.Done());
            Assert.AreEqual(LookPhase.Done, card.Phase);
            Assert.IsTrue(card.Finished);
        }

        [Test]
        public void The_fact_types_out_a_few_characters_at_a_time()
        {
            card.Apply(LookEvent.Named("Mug"));
            card.Apply(LookEvent.Fact(new string('x', 300)));
            Assert.AreEqual(0, card.Fact.Visible.Length);
            card.Advance(0.5f);
            Assert.That(card.Fact.Visible.Length, Is.InRange(1, 299));
            Assert.IsTrue(card.Typing);
        }

        [Test]
        public void An_error_fails_the_card_with_its_message()
        {
            card.Apply(LookEvent.Named("Red m", pending: true));
            card.Apply(LookEvent.Failure("timeout", "EDITH took too long."));
            Assert.AreEqual(LookPhase.Failed, card.Phase);
            Assert.AreEqual("EDITH took too long.", card.Error);
            Assert.IsTrue(card.Finished);
        }

        [Test]
        public void A_rarity_before_the_name_sets_the_tier_and_leaves_the_phase()
        {
            card.Apply(LookEvent.Rated(Rarity.Epic));
            Assert.AreEqual(Rarity.Epic, card.Tier);
            Assert.AreEqual(LookPhase.Looking, card.Phase);
            card.Apply(LookEvent.Named("Lamp"));
            Assert.AreEqual(LookPhase.Named, card.Phase);
            Assert.AreEqual(Rarity.Epic, card.Tier);
        }

        [Test]
        public void A_fact_before_the_final_name_still_names_the_card()
        {
            card.Apply(LookEvent.Named("Red m", pending: true));
            card.Apply(LookEvent.Fact("Mugs are old."));
            Assert.AreEqual(LookPhase.Named, card.Phase);
            card.Apply(LookEvent.Named("Red mug"));
            Assert.AreEqual("Red mug", card.Name);
        }

        [Test]
        public void A_pending_name_after_the_final_one_does_not_take_it_back()
        {
            card.Apply(LookEvent.Named("Red mug"));
            card.Apply(LookEvent.Named("Red m", pending: true));
            Assert.AreEqual(LookPhase.Named, card.Phase);
            Assert.AreEqual("Red mug", card.Name);
        }

        [Test]
        public void Events_after_done_are_ignored()
        {
            card.Apply(LookEvent.Named("Mug"));
            card.Apply(LookEvent.Done());
            card.Apply(LookEvent.Named("Cup"));
            card.Apply(LookEvent.Fact("late"));
            card.Apply(LookEvent.Rated(Rarity.Mythic));
            card.Apply(LookEvent.Failure("network", "late"));
            Assert.AreEqual(LookPhase.Done, card.Phase);
            Assert.AreEqual("Mug", card.Name);
            Assert.IsNull(card.Tier);
            card.Advance(10f);
            Assert.AreEqual("Mug", card.Fact.Visible, "a Look that ends without a fact shows its name");
        }

        [Test]
        public void A_fact_is_shown_instead_of_the_name()
        {
            card.Apply(LookEvent.Named("Mug"));
            card.Apply(LookEvent.Fact("Mugs are old."));
            card.Apply(LookEvent.Done());
            card.Advance(10f);
            Assert.AreEqual("Mugs are old.", card.Fact.Visible);
        }

        [Test]
        public void Events_after_an_error_are_ignored()
        {
            card.Apply(LookEvent.Failure("network", "EDITH cannot be reached."));
            card.Apply(LookEvent.Named("Mug"));
            card.Apply(LookEvent.Done());
            Assert.AreEqual(LookPhase.Failed, card.Phase);
            Assert.AreEqual("", card.Name);
        }

        [Test]
        public void A_fact_that_is_markup_cannot_open_a_tag()
        {
            card.Apply(LookEvent.Fact("a <b>bold</b> claim"));
            card.Advance(10f);
            StringAssert.DoesNotContain("<", card.Fact.Visible);
        }
    }
}
