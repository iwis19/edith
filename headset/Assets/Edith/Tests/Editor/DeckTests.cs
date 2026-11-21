using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class DeckTests
    {
        const float Dt = 1f / 72f;

        static void Settle(Deck deck, float seconds = 2f)
        {
            for (var elapsed = 0f; elapsed < seconds; elapsed += Dt) deck.Advance(Dt);
        }

        [Test]
        public void It_opens_on_the_newest_card()
        {
            var deck = new Deck(5);
            Assert.AreEqual(0, deck.Target);
            Assert.AreEqual(0f, deck.Position);
        }

        [Test]
        public void A_step_moves_one_card_and_the_deck_eases_there()
        {
            var deck = new Deck(5);
            deck.Step(1);
            Assert.AreEqual(1, deck.Target);
            Assert.AreEqual(0f, deck.Position, "the cards have not moved yet");
            deck.Advance(Dt);
            Assert.That(deck.Position, Is.GreaterThan(0f).And.LessThan(1f));
            Settle(deck);
            Assert.AreEqual(1f, deck.Position);
        }

        [Test]
        public void The_deck_never_overshoots_its_target()
        {
            var deck = new Deck(5);
            deck.JumpTo(4);
            var last = 0f;
            for (var i = 0; i < 200; i++)
            {
                deck.Advance(Dt);
                Assert.That(deck.Position, Is.GreaterThanOrEqualTo(last).And.LessThanOrEqualTo(4f));
                last = deck.Position;
            }
            Assert.AreEqual(4f, deck.Position);
        }

        [Test]
        public void A_deck_that_replaces_another_forms_over_a_fifth_of_a_second()
        {
            var deck = new Deck(5, 2, true);
            Assert.AreEqual(0f, deck.Form);
            deck.Advance(0.1f);
            Assert.That(deck.Form, Is.GreaterThan(0f).And.LessThan(1f));
            deck.Advance(0.11f);
            Assert.AreEqual(1f, deck.Form);
            Assert.AreEqual(2f, deck.Position, "forming never moves the middle card");
        }

        [Test]
        public void A_new_deck_is_formed_at_once()
        {
            Assert.AreEqual(1f, new Deck(5).Form);
        }

        [Test]
        public void Steps_stop_at_both_ends()
        {
            var deck = new Deck(3);
            deck.Step(-1);
            Assert.AreEqual(0, deck.Target);
            for (var i = 0; i < 5; i++) deck.Step(1);
            Assert.AreEqual(2, deck.Target);
        }

        [Test]
        public void A_jump_past_the_end_lands_on_the_last_card()
        {
            var deck = new Deck(3);
            deck.JumpTo(10);
            Assert.AreEqual(2, deck.Target);
            deck.JumpTo(-4);
            Assert.AreEqual(0, deck.Target);
        }

        [Test]
        public void A_scrub_moves_the_cards_with_the_hand_and_holds_them_there()
        {
            var deck = new Deck(5);
            deck.Scrub(1.3f);
            Assert.AreEqual(1.3f, deck.Position, 1e-5f);
            Settle(deck, 0.5f);
            Assert.AreEqual(1.3f, deck.Position, 1e-5f, "the deck waits for the hand while it is held");
        }

        [Test]
        public void Letting_go_snaps_to_the_nearest_card()
        {
            var deck = new Deck(5);
            deck.Scrub(1.3f);
            deck.Release();
            Assert.AreEqual(1, deck.Target);
            Settle(deck);
            Assert.AreEqual(1f, deck.Position);

            deck.Scrub(0.6f);
            deck.Release();
            Assert.AreEqual(2, deck.Target);
        }

        [Test]
        public void A_scrub_stops_at_both_ends()
        {
            var deck = new Deck(3);
            deck.Scrub(-2f);
            Assert.AreEqual(0f, deck.Position);
            deck.Scrub(9f);
            Assert.AreEqual(2f, deck.Position);
            deck.Release();
            Assert.AreEqual(2, deck.Target);
        }

        [Test]
        public void A_release_without_a_scrub_keeps_the_target()
        {
            var deck = new Deck(5);
            deck.Step(1);
            deck.Release();
            Assert.AreEqual(1, deck.Target);
        }

        [Test]
        public void A_deck_can_start_on_any_card()
        {
            var deck = new Deck(5, 3);
            Assert.AreEqual(3, deck.Target);
            Assert.AreEqual(3f, deck.Position);
        }

        [Test]
        public void An_empty_deck_stays_at_zero()
        {
            var deck = new Deck(0);
            deck.Step(1);
            deck.Scrub(2f);
            deck.Release();
            Settle(deck, 0.2f);
            Assert.AreEqual(0, deck.Target);
            Assert.AreEqual(0f, deck.Position);
        }
    }
}
