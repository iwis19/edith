using System.Collections.Generic;
using System.Linq;
using Edith.Hands;
using Edith.Pinch;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PinchTrackerTests
    {
        const float Dt = 1f / 72f;

        PinchTracker tracker;
        float time;
        List<(float time, PinchReading reading)> log;

        [SetUp]
        public void SetUp()
        {
            tracker = new PinchTracker(new PinchTuning());
            time = 0f;
            log = new List<(float, PinchReading)>();
        }

        /// A right hand with its palm facing away from an eye at the origin looking down +z.
        static HandFrame Hand(float t, Vector3 point, bool pinching, float gap = 0.01f, bool tracked = true, bool palmTowardEye = false)
        {
            var wrist = point + new Vector3(0.01f, -0.1f, -0.03f);
            var index = wrist + new Vector3(-0.03f, 0.08f, 0f);
            var little = wrist + new Vector3(0.03f, 0.07f, 0f);
            return new HandFrame
            {
                Time = t,
                EyePosition = Vector3.zero,
                EyeRotation = Quaternion.identity,
                Tracked = tracked,
                Pinching = pinching,
                ThumbTip = point + new Vector3(0f, -gap * 0.5f, 0f),
                IndexTip = point + new Vector3(0f, gap * 0.5f, 0f),
                Wrist = wrist,
                IndexKnuckle = palmTowardEye ? little : index,
                LittleKnuckle = palmTowardEye ? index : little,
            };
        }

        void Feed(Vector3 point, bool pinching, float seconds = Dt, float gap = 0.01f, bool tracked = true, bool palmTowardEye = false)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                log.Add((time, tracker.Step(Hand(time, point, pinching, gap, tracked, palmTowardEye))));
                time += Dt;
            }
        }

        void Drag(Vector3 from, Vector3 to, float seconds = 0.4f)
        {
            var steps = Mathf.RoundToInt(seconds / Dt);
            for (var i = 0; i <= steps; i++) Feed(Vector3.Lerp(from, to, i / (float)steps), true);
        }

        List<PinchEvent> Events() => log.Select(e => e.reading.Event).Where(e => e != PinchEvent.None).ToList();

        PinchReading Last(PinchEvent pinchEvent) => log.Last(e => e.reading.Event == pinchEvent).reading;

        static readonly Vector3 From = new Vector3(-0.1f, -0.05f, 0.45f);
        static readonly Vector3 To = new Vector3(0.1f, 0.1f, 0.45f);

        [Test]
        public void A_short_pinch_is_a_tap_where_it_landed()
        {
            Feed(From, true, 0.2f);
            Feed(From + new Vector3(0.02f, 0f, 0f), true, 0.1f);
            Feed(From, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Tap }, Events());
            Assert.That(Vector3.Distance(Last(PinchEvent.Tap).Start, From), Is.LessThan(1e-4f));
        }

        [Test]
        public void A_drag_completes_on_release_and_is_confirmed_once_the_rejoin_window_passes()
        {
            Drag(From, To);
            Feed(To, false, 0.6f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());
            var completed = log.First(e => e.reading.Event == PinchEvent.Completed);
            var confirmed = log.First(e => e.reading.Event == PinchEvent.Confirmed);
            Assert.That(confirmed.time - completed.time, Is.InRange(0.3f, 0.42f));

            var box = confirmed.reading.Box.Tangent;
            Assert.That(box.xMin, Is.EqualTo(From.x / From.z).Within(1e-3f));
            Assert.That(box.yMax, Is.EqualTo(To.y / To.z).Within(1e-3f));
            Assert.That(confirmed.reading.Box.Depth, Is.EqualTo(0.45f).Within(1e-3f));
        }

        [Test]
        public void A_pinch_flicker_shorter_than_the_release_time_is_ignored()
        {
            Drag(From, Vector3.Lerp(From, To, 0.5f));
            Feed(Vector3.Lerp(From, To, 0.5f), false, 2 * Dt);
            Drag(Vector3.Lerp(From, To, 0.5f), To);
            Feed(To, false, 0.6f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());
            Assert.That(Last(PinchEvent.Confirmed).Box.Tangent.xMax, Is.EqualTo(To.x / To.z).Within(1e-3f));
        }

        [Test]
        public void A_dropout_that_comes_back_nearby_resumes_the_same_drag()
        {
            var mid = Vector3.Lerp(From, To, 0.5f);
            Drag(From, mid);
            Feed(mid, false, 0.25f);
            Drag(mid + new Vector3(0.03f, 0f, 0f), To);
            Feed(To, false, 0.6f);

            CollectionAssert.AreEqual(
                new[] { PinchEvent.Completed, PinchEvent.Resumed, PinchEvent.Completed, PinchEvent.Confirmed }, Events());
            Assert.That(Last(PinchEvent.Confirmed).Box.Tangent.xMin, Is.EqualTo(From.x / From.z).Within(1e-3f),
                "the resumed drag keeps its original start");
        }

        [Test]
        public void A_new_pinch_far_away_confirms_the_release_and_starts_at_once()
        {
            Drag(From, To);
            Feed(To, false, 0.15f);
            var elsewhere = new Vector3(0.1f, -0.15f, 0.45f);
            Feed(elsewhere, true);

            CollectionAssert.AreEqual(new[] { PinchEvent.Completed, PinchEvent.Confirmed }, Events());
            var confirmed = log.Last().reading;
            Assert.AreEqual(PinchEvent.Confirmed, confirmed.Event);
            Assert.AreEqual(PinchPhase.Pressed, confirmed.Phase);
            Assert.That(confirmed.Start.x, Is.EqualTo(From.x).Within(1e-4f), "Confirmed reports the released drag's start");
        }

        [Test]
        public void The_box_ends_where_the_fingers_were_still_together()
        {
            Drag(From, To);
            // The fingertips part and the hand falls before the pinch flag drops.
            Feed(To + new Vector3(0f, -0.01f, 0f), true, Dt, 0.04f);
            Feed(To + new Vector3(0f, -0.03f, 0f), true, Dt, 0.06f);
            Feed(To + new Vector3(0f, -0.06f, 0f), false, 0.6f, 0.08f);

            var box = Last(PinchEvent.Confirmed).Box.Tangent;
            Assert.That(box.yMax, Is.EqualTo(To.y / To.z).Within(1e-3f));
        }

        [Test]
        public void A_pinch_with_the_palm_toward_the_eye_is_ignored()
        {
            Feed(From, true, 0.1f, palmTowardEye: true);
            Drag(From, To);

            Assert.AreEqual(PinchRejection.PalmFacingUser, log[0].reading.Rejection);
            Assert.AreEqual(PinchPhase.Idle, log[0].reading.Phase);
        }

        [Test]
        public void Losing_the_hand_mid_drag_cancels_it()
        {
            Drag(From, To);
            Feed(To, true, 0.3f, tracked: false);

            CollectionAssert.AreEqual(new[] { PinchEvent.Cancelled }, Events());
            Assert.AreEqual(PinchRejection.Lost, Last(PinchEvent.Cancelled).Rejection);
        }

        [Test]
        public void A_tiny_box_is_cancelled_on_release()
        {
            Drag(From, From + new Vector3(0.06f, 0.01f, 0f));
            Feed(From, false, 0.2f);

            CollectionAssert.AreEqual(new[] { PinchEvent.Cancelled }, Events());
        }
    }
}
