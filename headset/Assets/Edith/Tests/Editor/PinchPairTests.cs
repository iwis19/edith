using System.Collections.Generic;
using System.Linq;
using Edith.Hands;
using Edith.Pinch;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PinchPairTests
    {
        const float Dt = 1f / 72f;

        static readonly Vector3 From = new Vector3(-0.1f, -0.05f, 0.45f);
        static readonly Vector3 To = new Vector3(0.1f, 0.1f, 0.45f);
        static readonly Vector3 Aside = new Vector3(-0.3f, -0.2f, 0.4f);

        PinchPair pair;
        float time;
        List<PinchReading> log;

        [SetUp]
        public void SetUp()
        {
            pair = new PinchPair(new PinchTuning());
            time = 0f;
            log = new List<PinchReading>();
        }

        static HandFrame Right(float t, Vector3 point, bool pinching) => PinchTrackerTests.Hand(t, point, pinching);

        /// A left hand is the mirror of a right one, so its knuckles swap sides for the same palm direction.
        static HandFrame Left(float t, Vector3 point, bool pinching, bool palmTowardEye = false)
        {
            var hand = PinchTrackerTests.Hand(t, point, pinching, palmTowardEye: !palmTowardEye);
            hand.Side = HandSide.Left;
            return hand;
        }

        void Feed(Vector3 right, bool rightPinching, Vector3 left, bool leftPinching, float seconds = Dt, bool leftPalmTowardEye = false)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                log.Add(pair.Step(Right(time, right, rightPinching), Left(time, left, leftPinching, leftPalmTowardEye)));
                time += Dt;
            }
        }

        void DragRight(Vector3 left, bool leftPinching)
        {
            const int steps = 29;
            for (var i = 0; i <= steps; i++) Feed(Vector3.Lerp(From, To, i / (float)steps), true, left, leftPinching);
        }

        void DragLeft()
        {
            const int steps = 29;
            for (var i = 0; i <= steps; i++) Feed(Aside, false, Vector3.Lerp(From, To, i / (float)steps), true);
        }

        List<PinchEvent> Events() => log.Select(r => r.Event).Where(e => e != PinchEvent.None).ToList();

        [Test]
        public void The_left_hand_alone_draws_a_box()
        {
            DragLeft();
            Assert.AreEqual(HandSide.Left, pair.Owner);
            Feed(Aside, false, To, true, 0.15f);
            Feed(Aside, false, To, false, 0.6f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());
            var box = log.Last(r => r.Event == PinchEvent.Confirmed).Box.Tangent;
            Assert.That(box.width, Is.EqualTo(0.2f / 0.45f).Within(0.03f));
            Assert.IsNull(pair.Owner);
        }

        [Test]
        public void A_left_tap_lands_where_the_left_hand_pinched()
        {
            Feed(Aside, false, From, true, 0.2f);
            Feed(Aside, false, From, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Tap }, Events());
            Assert.That(Vector3.Distance(log.Last(r => r.Event == PinchEvent.Tap).Start, From), Is.LessThan(1e-4f));
        }

        [Test]
        public void A_left_pinch_with_the_palm_toward_the_eye_is_ignored()
        {
            Feed(Aside, false, From, true, 0.3f, leftPalmTowardEye: true);
            Feed(Aside, false, From, false, 0.2f);

            Assert.IsEmpty(Events());
            Assert.IsTrue(log.Any(r => r.Rejection == PinchRejection.PalmFacingUser));
            Assert.IsTrue(log.All(r => r.Phase == PinchPhase.Idle));
        }

        [Test]
        public void While_one_hand_drags_the_other_hands_pinch_is_ignored_until_it_lets_go()
        {
            Feed(From, true, Aside, false);
            DragRight(Aside, leftPinching: true);
            Feed(To, false, Aside, true, 0.6f);
            Assert.IsNull(pair.Owner, "the right hand's box is confirmed");
            Feed(To, false, Aside, true, 0.3f);
            Feed(To, false, Aside, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());

            Feed(To, false, Aside, true, 0.2f);
            Feed(To, false, Aside, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed, PinchEvent.Tap }, Events());
        }

        [Test]
        public void A_tap_by_the_other_hand_inside_the_rejoin_window_is_ignored()
        {
            DragRight(Aside, leftPinching: false);
            Feed(To, false, Aside, false, 0.15f);
            Feed(To, false, Aside, true, 0.1f);
            Feed(To, false, Aside, false, 0.5f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());
        }

        [Test]
        public void When_both_hands_pinch_on_the_same_frame_the_right_hand_owns_it()
        {
            Feed(From, true, Aside, true, 0.2f);
            Assert.AreEqual(HandSide.Right, pair.Owner);
            Feed(From, false, Aside, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Tap }, Events());
            Assert.That(Vector3.Distance(log.Last(r => r.Event == PinchEvent.Tap).Start, From), Is.LessThan(1e-4f));
        }

        [Test]
        public void The_hands_take_turns()
        {
            Feed(From, true, Aside, false, 0.2f);
            Feed(From, false, Aside, false, 0.2f);
            Feed(From, false, Aside, true, 0.2f);
            Assert.AreEqual(HandSide.Left, pair.Owner);
            Feed(From, false, Aside, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Tap, PinchEvent.Tap }, Events());
            Assert.That(Vector3.Distance(log.Last(r => r.Event == PinchEvent.Tap).Start, Aside), Is.LessThan(1e-4f));
        }
    }
}
