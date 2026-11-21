using System.Collections.Generic;
using System.Linq;
using Edith.Grab;
using Edith.Hands;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;
using static Edith.Tests.SnapshotShelfTests;

namespace Edith.Tests
{
    public class GrabPairTests
    {
        const float Dt = 1f / 72f;
        const float Open = 1.9f;
        const float Fist = 1.0f;
        const int Card = 7;
        const int OtherCard = 8;

        static readonly Vector3 Pocket = new Vector3(0f, -0.30f, 0f);
        static readonly Vector3 AtCard = new Vector3(0f, 0f, 0.4f);
        static readonly Vector3 AtOtherCard = new Vector3(0.5f, 0f, 0.4f);
        static readonly Vector3 Away = new Vector3(-0.5f, -0.3f, 0.3f);

        GrabPair pair;
        float time;
        List<GrabReading> right, left;
        List<(int id, SightBox box)> cards;

        [SetUp]
        public void SetUp()
        {
            pair = new GrabPair(new GrabTuning());
            time = 0f;
            right = new List<GrabReading>();
            left = new List<GrabReading>();
            cards = new List<(int, SightBox)> { (Card, Box(0f)), (OtherCard, Box(0.5f)) };
        }

        void Feed(Vector3 rightGrip, float rightCurl, Vector3 leftGrip, float leftCurl, float seconds = Dt)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                var leftHand = GrabTrackerTests.Hand(time, leftGrip, leftCurl);
                leftHand.Side = HandSide.Left;
                var (r, l) = pair.Step(GrabTrackerTests.Hand(time, rightGrip, rightCurl), leftHand, cards);
                right.Add(r);
                left.Add(l);
                time += Dt;
            }
        }

        static List<GrabEvent> Events(List<GrabReading> log) => log.Select(r => r.Event).Where(e => e != GrabEvent.None).ToList();

        [Test]
        public void The_left_hand_alone_grabs_and_pockets_a_card()
        {
            Feed(Away, Open, AtCard, Open, 0.3f);
            Feed(Away, Open, AtCard, Fist, 0.2f);
            var pulled = AtCard + (Pocket - AtCard).normalized * 0.2f;
            for (var i = 0; i <= 28; i++) Feed(Away, Open, Vector3.Lerp(AtCard, pulled, i / 28f), Fist);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Pocketed }, Events(left));
            Assert.AreEqual(Card, left.Last(r => r.Event == GrabEvent.Pocketed).CardId);
            Assert.IsEmpty(Events(right));
            Assert.IsTrue(right.All(r => !r.Claimed));
        }

        [Test]
        public void A_card_one_hand_holds_cannot_be_grabbed_by_the_other()
        {
            Feed(AtCard, Open, Away, Open, 0.3f);
            Feed(AtCard, Fist, Away, Open, 0.2f);
            Feed(AtCard, Fist, AtCard, Open, 0.3f);
            Feed(AtCard, Fist, AtCard, Fist, 0.3f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events(right));
            Assert.IsEmpty(Events(left));
            Assert.IsFalse(left.Last().Claimed);
            Assert.AreEqual(GrabPhase.Holding, right.Last().Phase);
        }

        [Test]
        public void When_both_hands_close_on_one_card_in_the_same_frame_the_right_hand_takes_it()
        {
            Feed(AtCard, Open, AtCard, Open, 0.3f);
            Feed(AtCard, Fist, AtCard, Fist, 0.2f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events(right));
            Assert.IsEmpty(Events(left));
        }

        [Test]
        public void Each_hand_holds_its_own_card()
        {
            Feed(AtCard, Open, AtOtherCard, Open, 0.3f);
            Feed(AtCard, Fist, AtOtherCard, Fist, 0.2f);

            Assert.AreEqual(GrabPhase.Holding, right.Last().Phase);
            Assert.AreEqual(Card, right.Last().CardId);
            Assert.AreEqual(GrabPhase.Holding, left.Last().Phase);
            Assert.AreEqual(OtherCard, left.Last().CardId);
        }

        [Test]
        public void A_card_one_hand_let_go_can_be_grabbed_by_the_other()
        {
            Feed(AtCard, Open, Away, Open, 0.3f);
            Feed(AtCard, Fist, Away, Open, 0.2f);
            Feed(AtCard, Open, AtCard, Open, 0.3f);
            Feed(AtCard, Open, AtCard, Fist, 0.2f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Dropped }, Events(right));
            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events(left));
            Assert.AreEqual(Card, left.Last().CardId);
        }
    }
}
