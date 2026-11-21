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
    public class GrabTrackerTests
    {
        const float Dt = 1f / 72f;
        const float Open = 1.9f;
        const float Relaxed = 1.2f;
        const float Fist = 1.0f;
        const int Card = 7;

        static readonly Vector3 Pocket = new Vector3(0f, -0.30f, 0f);
        static readonly Vector3 AtCard = new Vector3(0f, 0f, 0.4f);

        GrabTracker tracker;
        float time;
        List<(float time, GrabReading reading)> log;
        List<(int id, SightBox box)> cards;

        [SetUp]
        public void SetUp()
        {
            tracker = new GrabTracker(new GrabTuning());
            time = 0f;
            log = new List<(float, GrabReading)>();
            cards = new List<(int, SightBox)> { (Card, Box(0f)) };
        }

        /// A right hand with its grip at `grip`, palm facing away from an eye at the origin looking down +z.
        /// Each fingertip sits at `curl` times its knuckle's distance from the wrist.
        internal static HandFrame Hand(float t, Vector3 grip, float curl, bool visible = true)
        {
            var wrist = grip - new Vector3(0f, 0.0675f, 0f);
            var index = wrist + new Vector3(-0.03f, 0.085f, 0f);
            var middle = wrist + new Vector3(0f, 0.09f, 0f);
            var ring = wrist + new Vector3(0.02f, 0.085f, 0f);
            var little = wrist + new Vector3(0.04f, 0.07f, 0f);
            return new HandFrame
            {
                Time = t,
                EyePosition = Vector3.zero,
                EyeRotation = Quaternion.identity,
                Visible = visible,
                Wrist = wrist,
                IndexKnuckle = index,
                IndexTip = wrist + (index - wrist) * curl,
                MiddleKnuckle = middle,
                MiddleTip = wrist + (middle - wrist) * curl,
                RingKnuckle = ring,
                RingTip = wrist + (ring - wrist) * curl,
                LittleKnuckle = little,
            };
        }

        void Feed(Vector3 grip, float curl, float seconds = Dt, bool visible = true)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                log.Add((time, tracker.Step(Hand(time, grip, curl, visible), cards)));
                time += Dt;
            }
        }

        void Move(Vector3 from, Vector3 to, float curl = Fist, float seconds = 0.4f)
        {
            var steps = Mathf.RoundToInt(seconds / Dt);
            for (var i = 0; i <= steps; i++) Feed(Vector3.Lerp(from, to, i / (float)steps), curl);
        }

        /// Opens the hand at `at`, then closes it there.
        void OpenThenClose(Vector3 at, float openSeconds = 0.3f)
        {
            Feed(at, Open, openSeconds);
            Feed(at, Fist, 0.2f);
        }

        /// A point `meters` from `from` on the straight line to the pocket.
        static Vector3 TowardPocket(Vector3 from, float meters) =>
            from + (Pocket - from).normalized * meters;

        List<GrabEvent> Events() => log.Select(e => e.reading.Event).Where(e => e != GrabEvent.None).ToList();

        GrabReading Last(GrabEvent grabEvent) => log.Last(e => e.reading.Event == grabEvent).reading;

        [Test]
        public void An_open_hand_closing_on_a_card_grabs_it()
        {
            OpenThenClose(AtCard);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events());
            var grabbed = Last(GrabEvent.Grabbed);
            Assert.AreEqual(Card, grabbed.CardId);
            Assert.IsTrue(grabbed.HasGrip);
            Assert.That(Vector3.Distance(grabbed.Grip, AtCard), Is.LessThan(1e-4f));
            Assert.AreEqual(GrabPhase.Holding, log.Last().reading.Phase);
            Assert.AreEqual(Card, log.Last().reading.CardId);
            Assert.IsTrue(log.Last().reading.Claimed);
        }

        [Test]
        public void A_fist_with_no_card_nearby_does_not_grab()
        {
            OpenThenClose(new Vector3(0.6f, 0f, 0.4f));

            CollectionAssert.IsEmpty(Events());
            Assert.IsFalse(log.Last().reading.Claimed);
        }

        [Test]
        public void A_fist_in_front_of_or_behind_a_card_does_not_grab()
        {
            OpenThenClose(new Vector3(0f, 0f, 0.6f));
            OpenThenClose(new Vector3(0f, 0f, 0.2f));

            CollectionAssert.IsEmpty(Events());
        }

        [Test]
        public void The_card_nearest_the_hand_in_depth_is_grabbed()
        {
            cards.Add((Card + 1, Box(0f, depth: 0.5f)));
            OpenThenClose(new Vector3(0f, 0f, 0.47f));

            Assert.AreEqual(Card + 1, Last(GrabEvent.Grabbed).CardId);
        }

        [Test]
        public void A_hand_that_only_relaxes_never_grabs()
        {
            Feed(AtCard, Relaxed, 1.5f);
            Feed(AtCard, 1.12f, 0.3f);

            CollectionAssert.IsEmpty(Events());
            Assert.IsFalse(log.Last().reading.Claimed);
        }

        [Test]
        public void A_hand_that_was_open_over_a_second_ago_does_not_grab()
        {
            Feed(AtCard, Open, 0.2f);
            Feed(AtCard, Relaxed, 1.2f);
            Feed(AtCard, Fist, 0.2f);

            CollectionAssert.IsEmpty(Events());
        }

        [Test]
        public void A_hand_that_was_open_half_a_second_ago_grabs()
        {
            Feed(AtCard, Open, 0.2f);
            Feed(AtCard, Relaxed, 0.5f);
            Feed(AtCard, Fist, 0.2f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events());
        }

        [Test]
        public void A_fist_from_the_first_frame_never_grabs()
        {
            Feed(AtCard, Fist, 0.5f);

            CollectionAssert.IsEmpty(Events());
        }

        [Test]
        public void Pulling_to_the_chest_pockets_once_and_opening_later_drops_nothing()
        {
            OpenThenClose(AtCard);
            Move(AtCard, Vector3.Lerp(AtCard, Pocket, 0.5f));
            Feed(Vector3.Lerp(AtCard, Pocket, 0.5f), Fist, 0.3f);
            Feed(Vector3.Lerp(AtCard, Pocket, 0.5f), Open, 0.3f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Pocketed }, Events());
            Assert.AreEqual(Card, Last(GrabEvent.Pocketed).CardId);
            Assert.AreEqual(GrabPhase.Idle, Last(GrabEvent.Pocketed).Phase);
            Assert.AreEqual(GrabPhase.Idle, log.Last().reading.Phase);
        }

        [Test]
        public void Pulling_straight_back_toward_the_eye_pockets()
        {
            OpenThenClose(AtCard);
            Move(AtCard, new Vector3(0f, 0f, 0.05f));

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Pocketed }, Events());
        }

        [Test]
        public void A_short_pull_on_a_distant_card_does_not_pocket()
        {
            var far = new Vector3(0f, 0f, 0.6f);
            cards[0] = (Card, Box(0f, depth: 0.6f));
            OpenThenClose(far);
            Move(far, TowardPocket(far, 0.12f));

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events());
        }

        [Test]
        public void A_card_drawn_close_pockets_with_a_shorter_pull()
        {
            var near = new Vector3(0f, -0.1f, 0.22f);
            cards[0] = (Card, Box(0f, -0.1f, depth: 0.22f));
            OpenThenClose(near);
            Move(near, TowardPocket(near, 0.12f));

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Pocketed }, Events());
        }

        [Test]
        public void Moving_sideways_or_away_never_pockets()
        {
            OpenThenClose(AtCard);
            Move(AtCard, new Vector3(0.45f, 0f, 0.4f));
            Move(new Vector3(0.45f, 0f, 0.4f), new Vector3(0.45f, 0.1f, 0.9f));

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events());
            Assert.AreEqual(GrabPhase.Holding, log.Last().reading.Phase);
        }

        [Test]
        public void Opening_the_hand_mid_hold_drops_the_card()
        {
            OpenThenClose(AtCard);
            Move(AtCard, new Vector3(0.05f, 0f, 0.35f), seconds: 0.2f);
            Feed(new Vector3(0.05f, 0f, 0.35f), Open, 0.3f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Dropped }, Events());
            Assert.AreEqual(Card, Last(GrabEvent.Dropped).CardId);
            Assert.AreEqual(GrabPhase.Idle, log.Last().reading.Phase);
            Assert.IsFalse(log.Last().reading.Claimed);
        }

        [Test]
        public void A_hand_in_between_open_and_closed_keeps_holding()
        {
            OpenThenClose(AtCard);
            Feed(AtCard, 1.3f, 0.5f);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed }, Events());
            Assert.AreEqual(GrabPhase.Holding, log.Last().reading.Phase);
        }

        [Test]
        public void A_brief_tracking_loss_keeps_the_hold()
        {
            OpenThenClose(AtCard);
            Feed(AtCard, Fist, 0.2f, visible: false);
            var during = log.Last().reading;
            Move(AtCard, new Vector3(0f, 0f, 0.05f));

            Assert.AreEqual(GrabPhase.Holding, during.Phase);
            Assert.IsFalse(during.HasGrip);
            Assert.IsTrue(during.Claimed);
            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Pocketed }, Events());
        }

        [Test]
        public void A_long_tracking_loss_drops_the_card_once()
        {
            OpenThenClose(AtCard);
            Feed(AtCard, Fist, 0.6f, visible: false);

            CollectionAssert.AreEqual(new[] { GrabEvent.Grabbed, GrabEvent.Dropped }, Events());
            Assert.AreEqual(Card, Last(GrabEvent.Dropped).CardId);
            Assert.IsFalse(log.Last().reading.Claimed);
        }

        [Test]
        public void The_hand_stays_claimed_from_the_grab_until_it_opens_after_a_pocket()
        {
            OpenThenClose(AtCard);
            Move(AtCard, Vector3.Lerp(AtCard, Pocket, 0.5f));
            Feed(Vector3.Lerp(AtCard, Pocket, 0.5f), Fist, 0.3f);
            var grabbed = log.FindIndex(e => e.reading.Event == GrabEvent.Grabbed);
            Feed(Vector3.Lerp(AtCard, Pocket, 0.5f), Open, 0.3f);
            var opened = log.FindIndex(grabbed, e => !e.reading.Claimed);

            Assert.That(opened, Is.GreaterThan(log.FindIndex(e => e.reading.Event == GrabEvent.Pocketed)));
            Assert.IsTrue(log.Skip(grabbed).Take(opened - grabbed).All(e => e.reading.Claimed));
            Assert.IsTrue(log.Skip(opened).All(e => !e.reading.Claimed));
        }

        [Test]
        public void The_reading_turns_with_the_hand()
        {
            Feed(AtCard, Open, 0.1f);
            var flat = log.Last().reading.Rotation;
            var turned = Hand(time, AtCard, Open);
            var pivot = turned.Wrist;
            var roll = Quaternion.AngleAxis(60f, Vector3.forward);
            turned.IndexKnuckle = pivot + roll * (turned.IndexKnuckle - pivot);
            turned.IndexTip = pivot + roll * (turned.IndexTip - pivot);
            turned.MiddleKnuckle = pivot + roll * (turned.MiddleKnuckle - pivot);
            turned.MiddleTip = pivot + roll * (turned.MiddleTip - pivot);
            turned.RingKnuckle = pivot + roll * (turned.RingKnuckle - pivot);
            turned.RingTip = pivot + roll * (turned.RingTip - pivot);
            turned.LittleKnuckle = pivot + roll * (turned.LittleKnuckle - pivot);

            var reading = tracker.Step(turned, cards);

            Assert.That(Quaternion.Angle(flat, reading.Rotation), Is.EqualTo(60f).Within(0.5f));
        }

        [Test]
        public void A_closed_hand_remembers_its_deepest_curl_until_it_opens()
        {
            Feed(AtCard, Open, 0.3f);
            Assert.IsFalse(tracker.IsClosed);
            Feed(AtCard, 1.10f, 0.1f);
            Feed(AtCard, 0.95f, 0.1f);
            Feed(AtCard, 1.05f, 0.1f);
            Assert.IsTrue(tracker.IsClosed);
            Assert.That(tracker.DeepestCurl, Is.EqualTo(0.95f).Within(1e-3f));

            Feed(AtCard, Open, Dt);

            Assert.IsFalse(tracker.IsClosed);
            Assert.That(tracker.LastCurl, Is.EqualTo(Open).Within(1e-3f));
            Assert.That(tracker.DeepestCurl, Is.EqualTo(0.95f).Within(1e-3f));
        }

        [Test]
        public void A_second_fist_starts_its_deepest_curl_afresh()
        {
            Feed(AtCard, Open, 0.3f);
            Feed(AtCard, 0.9f, 0.2f);
            Feed(AtCard, Open, 0.3f);

            Feed(AtCard, 1.1f, 0.2f);

            Assert.That(tracker.DeepestCurl, Is.EqualTo(1.1f).Within(1e-3f));
        }
    }
}
