using Edith.Hands;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class RaySelectTests
    {
        const float Frame = 1f / 72f;

        // Target 1 is anything to the left of x = 0 one metre out, target 2 to the right.
        static int Targets(Ray ray, out float distance)
        {
            distance = 1f;
            var at = ray.GetPoint(1f);
            return at.x < 0f ? 1 : 2;
        }

        static int Nothing(Ray ray, out float distance)
        {
            distance = 0f;
            return -1;
        }

        static HandFrame Hand(HandSide side, float time, bool pinching, float aimX = -0.2f, bool pointing = true)
        {
            return new HandFrame
            {
                Side = side,
                Time = time,
                Tracked = pointing,
                Visible = pointing,
                Pinching = pinching && pointing,
                Pointing = pointing,
                PointerOrigin = Vector3.zero,
                PointerRotation = Quaternion.LookRotation(new Vector3(aimX, 0f, 1f)),
                PinchStrength = pinching ? 1f : 0f,
            };
        }

        static HandFrame Away(HandSide side, float time) => Hand(side, time, false, pointing: false);

        /// Steps the right hand through `frames` pinch flags from `time`, the left hand away.
        static RayReading Run(RaySelect select, ref float time, bool pinching, int frames, float aimX = -0.2f, RayTargets targets = null)
        {
            RayReading reading = default;
            for (var i = 0; i < frames; i++, time += Frame)
            {
                reading = select.Step(Hand(HandSide.Right, time, pinching, aimX), Away(HandSide.Left, time), targets ?? Targets);
                if (reading.Event != RayEvent.None) return reading;
            }
            return reading;
        }

        static RaySelect Started(ref float time)
        {
            var select = new RaySelect();
            Run(select, ref time, false, 3);
            return select;
        }

        [Test]
        public void A_pinch_let_go_on_the_same_target_clicks_it()
        {
            var time = 0f;
            var select = Started(ref time);
            var pressed = Run(select, ref time, true, 1);
            Assert.AreEqual(RayEvent.Pressed, pressed.Event);
            Assert.AreEqual(1, pressed.Pressed);
            Assert.AreEqual(HandSide.Right, pressed.Owner);
            Run(select, ref time, true, 10);
            var clicked = Run(select, ref time, false, 20);
            Assert.AreEqual(RayEvent.Clicked, clicked.Event);
            Assert.AreEqual(1, clicked.Pressed);
            Assert.IsNull(clicked.Owner);
        }

        [Test]
        public void Letting_go_on_another_target_clicks_nothing()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, true, 5);
            Run(select, ref time, true, 5, aimX: 0.2f);
            Assert.AreEqual(RayEvent.Released, Run(select, ref time, false, 20, aimX: 0.2f).Event);
        }

        [Test]
        public void The_target_is_read_when_the_fingers_start_to_open()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, true, 5);
            // The ray swings onto target 2 while the flag is already off but the release is not yet confirmed.
            Run(select, ref time, false, 1);
            Assert.AreEqual(RayEvent.Clicked, Run(select, ref time, false, 20, aimX: 0.2f).Event);
        }

        [Test]
        public void A_flicker_of_the_pinch_flag_is_not_a_release()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, true, 5);
            Assert.AreEqual(RayEvent.None, Run(select, ref time, false, 3).Event);
            Assert.AreEqual(RayEvent.None, Run(select, ref time, true, 5).Event);
            Assert.AreEqual(HandSide.Right, select.Step(Hand(HandSide.Right, time, true), Away(HandSide.Left, time), Targets).Owner);
        }

        [Test]
        public void A_pinch_held_from_before_the_page_opened_selects_nothing()
        {
            var time = 0f;
            var select = new RaySelect();
            Assert.AreEqual(RayEvent.None, Run(select, ref time, true, 10).Event);
            Assert.AreEqual(RayEvent.None, Run(select, ref time, false, 10).Event);
            Assert.AreEqual(RayEvent.Pressed, Run(select, ref time, true, 1).Event);
        }

        [Test]
        public void A_pinch_on_nothing_ends_without_a_click()
        {
            var time = 0f;
            var select = Started(ref time);
            Assert.AreEqual(-1, Run(select, ref time, true, 1, targets: Nothing).Pressed);
            Assert.AreEqual(RayEvent.Released, Run(select, ref time, false, 20, targets: Nothing).Event);
        }

        [Test]
        public void A_hand_lost_mid_pinch_cancels_it()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, true, 5);
            RayReading reading = default;
            for (var i = 0; i < 30 && reading.Event == RayEvent.None; i++, time += Frame)
                reading = select.Step(Away(HandSide.Right, time), Away(HandSide.Left, time), Targets);
            Assert.AreEqual(RayEvent.Cancelled, reading.Event);
        }

        [Test]
        public void One_hand_selects_at_a_time()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, true, 2);
            var reading = select.Step(Hand(HandSide.Right, time, true), Hand(HandSide.Left, time, true, 0.2f), Targets);
            Assert.AreEqual(HandSide.Right, reading.Owner);
            Assert.AreEqual(1, reading.Hovered);
            Assert.IsTrue(select.Aim(HandSide.Right).Held);
            Assert.IsFalse(select.Aim(HandSide.Left).Held);
        }

        [Test]
        public void A_ray_on_nothing_is_drawn_short_with_no_cursor()
        {
            var time = 0f;
            var select = Started(ref time);
            Run(select, ref time, false, 1, targets: Nothing);
            var aim = select.Aim(HandSide.Right);
            Assert.IsTrue(aim.Shown);
            Assert.IsFalse(aim.OnTarget);
            Assert.AreEqual(RaySelect.FreeLength, aim.Distance);
            Assert.IsFalse(select.Aim(HandSide.Left).Shown);
        }
    }
}
