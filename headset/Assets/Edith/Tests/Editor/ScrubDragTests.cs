using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class ScrubDragTests
    {
        const float Trail = ScrubTuning.DeadbandMeters / ScrubTuning.MetersPerCard * 0.5f;

        static ScrubDrag Drag(float start = 0f) => new ScrubDrag(start, 9);

        [Test]
        public void The_deck_follows_a_hand_that_moves_on_a_little_behind_it()
        {
            var drag = Drag();
            drag.Move(0.6f, 0f);
            drag.Move(0.6f, 0.05f);
            Assert.AreEqual(1.2f - Trail, drag.Position, 1e-5f);
        }

        [Test]
        public void A_reversal_inside_the_deadband_does_not_move_the_deck()
        {
            var drag = Drag();
            drag.Move(1f, 0f);
            var held = drag.Position;
            drag.Move(-0.1f, 0.05f);
            drag.Move(-0.1f, 0.1f);
            Assert.AreEqual(held, drag.Position, 1e-5f);
            drag.Move(0.15f, 0.15f);
            Assert.AreEqual(held, drag.Position, 1e-5f, "back to where the hand was is still not past the furthest point");
        }

        [Test]
        public void A_reversal_past_the_deadband_carries_the_deck_back_without_a_jump()
        {
            var drag = Drag();
            drag.Move(1f, 0f);
            var held = drag.Position;
            drag.Move(-0.3f, 0.05f);
            Assert.That(drag.Position, Is.LessThan(held));
            Assert.That(held - drag.Position, Is.LessThan(0.1f), "it left the deadband smoothly");
        }

        [Test]
        public void Release_undoes_the_last_tenth_of_a_second_of_movement()
        {
            var drag = Drag();
            drag.Move(0.4f, 0f);
            drag.Move(0.4f, 0.3f);
            drag.Move(0.7f, 0.9f);
            Assert.AreEqual(1, drag.Land(1.0f), "the move 0.1 s before the release is undone");
            Assert.AreEqual(2, drag.Land(1.1f), "past the lookback it counts");
        }

        [Test]
        public void A_drag_of_a_third_of_a_card_with_a_wobble_back_at_the_end_lands_one_card_forward()
        {
            var drag = Drag();
            drag.Move(0.4f, 0f);
            drag.Move(-0.26f, 0.5f);
            // The wobble has been held longer than the lookback.
            drag.Move(0f, 1.0f);
            Assert.AreEqual(1, drag.Land(1.5f));
        }

        [Test]
        public void A_final_wobble_inside_the_lookback_is_discarded()
        {
            var drag = Drag();
            drag.Move(0.6f, 0f);
            drag.Move(-0.5f, 0.95f);
            Assert.AreEqual(1, drag.Land(1.0f));
        }

        [Test]
        public void A_drag_forward_that_comes_back_a_card_lands_back()
        {
            var drag = Drag(3f);
            drag.Move(1f, 0f);
            drag.Move(-1.4f, 0.4f);
            Assert.AreEqual(3, drag.Land(1.5f));
        }

        [Test]
        public void A_backward_drag_lands_on_the_next_card_behind()
        {
            var drag = Drag(3f);
            drag.Move(-0.5f, 0f);
            Assert.AreEqual(2, drag.Land(1f));
        }

        [Test]
        public void A_drag_shorter_than_a_quarter_card_stays_on_its_card()
        {
            var drag = Drag(3f);
            drag.Move(0.3f, 0f);
            Assert.AreEqual(3, drag.Land(1f), "the deck trails the hand, so it moved 0.19 of a card");
        }

        [Test]
        public void A_drag_never_lands_beyond_the_ends()
        {
            var drag = new ScrubDrag(2f, 2);
            drag.Move(3f, 0f);
            Assert.AreEqual(2, drag.Land(1f));
        }
    }
}
