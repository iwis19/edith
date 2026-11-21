using Edith.Hands;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class WristButtonTests
    {
        const float Dt = 1f / 72f;
        static readonly Vector3 Wrist = new Vector3(-0.1f, -0.2f, 0.35f);
        static readonly Vector3 Far = new Vector3(0.2f, -0.2f, 0.35f);

        WristTuning tuning;
        WristButton button;
        float time;
        int presses;
        WristReading last;

        [SetUp]
        public void SetUp()
        {
            tuning = new WristTuning();
            button = new WristButton(tuning);
            time = 0f;
            presses = 0;
            last = default;
        }

        Vector3 Button => Wrist + Vector3.up * tuning.aboveWristMeters;

        /// A left hand with its fingers up, seen from an eye at the origin looking along +z. Turned 0 degrees its
        /// palm faces the eye; turned 180 its back does.
        HandFrame Left(float turned = 0f, bool tracked = true)
        {
            var turn = Quaternion.Euler(0f, turned, 0f);
            return new HandFrame
            {
                Time = time,
                EyeRotation = Quaternion.identity,
                Tracked = tracked,
                Visible = tracked,
                Wrist = Wrist,
                // Palm toward the eye, a left hand's thumb and index are on the left.
                IndexKnuckle = Wrist + turn * new Vector3(-0.03f, 0.09f, 0f),
                MiddleKnuckle = Wrist + turn * new Vector3(0f, 0.09f, 0f),
                LittleKnuckle = Wrist + turn * new Vector3(0.03f, 0.08f, 0f),
            };
        }

        HandFrame Right(Vector3 indexTip, bool tracked = true) =>
            new HandFrame { Time = time, EyeRotation = Quaternion.identity, Tracked = tracked, Visible = tracked, IndexTip = indexTip };

        void Feed(float seconds, Vector3 indexTip, float turned = 0f, bool leftTracked = true, bool rightTracked = true)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                last = button.Step(Left(turned, leftTracked), Right(indexTip, rightTracked));
                if (last.Pressed) presses++;
                time += Dt;
            }
        }

        [Test]
        public void A_palm_turned_to_the_eye_shows_the_button_above_the_wrist()
        {
            Feed(0.5f, Far);
            Assert.IsTrue(last.Shown);
            Assert.That(Vector3.Distance(last.Position, Button), Is.LessThan(1e-4f));
        }

        [Test]
        public void The_button_waits_a_moment_before_it_shows()
        {
            Feed(0.15f, Far);
            Assert.IsFalse(last.Shown);
            Feed(0.2f, Far);
            Assert.IsTrue(last.Shown);
        }

        [Test]
        public void The_back_of_the_hand_shows_no_button()
        {
            Feed(1f, Far, turned: 180f);
            Assert.IsFalse(last.Shown);
        }

        [Test]
        public void A_palm_turned_sideways_shows_no_button()
        {
            Feed(1f, Far, turned: 80f);
            Assert.IsFalse(last.Shown);
        }

        [Test]
        public void Turning_the_palm_away_hides_the_button()
        {
            Feed(0.5f, Far);
            Feed(0.5f, Far, turned: 180f);
            Assert.IsFalse(last.Shown);
        }

        [Test]
        public void A_hand_lost_for_an_instant_keeps_the_button()
        {
            Feed(0.5f, Far);
            Feed(0.1f, Far, leftTracked: false);
            Assert.IsTrue(last.Shown);
            Feed(0.5f, Far, leftTracked: false);
            Assert.IsFalse(last.Shown);
        }

        [Test]
        public void A_fingertip_on_the_button_presses_it_once()
        {
            Feed(0.5f, Far);
            Feed(0.5f, Button + new Vector3(0.01f, 0f, 0f));
            Assert.AreEqual(1, presses);
        }

        [Test]
        public void A_fingertip_that_only_comes_near_presses_nothing()
        {
            Feed(0.5f, Far);
            Feed(0.5f, Button + new Vector3(0.025f, 0f, 0f));
            Assert.AreEqual(0, presses);
        }

        [Test]
        public void The_fingertip_must_leave_before_it_can_press_again()
        {
            Feed(0.5f, Far);
            Feed(0.2f, Button);
            Feed(0.2f, Button + new Vector3(0.025f, 0f, 0f));
            Feed(0.2f, Button);
            Assert.AreEqual(1, presses, "it only drew back to inside the rearm distance");

            Feed(0.2f, Button + new Vector3(0.04f, 0f, 0f));
            Feed(0.2f, Button);
            Assert.AreEqual(2, presses);
        }

        [Test]
        public void No_button_no_press()
        {
            Feed(0.5f, Far, turned: 180f);
            Feed(0.5f, Wrist + Vector3.up * tuning.aboveWristMeters, turned: 180f);
            Assert.AreEqual(0, presses);
        }

        [Test]
        public void A_finger_already_there_when_the_button_shows_does_not_press()
        {
            Feed(1f, Button);
            Assert.IsTrue(last.Shown);
            Assert.AreEqual(0, presses);
        }

        [Test]
        public void An_untracked_fingertip_presses_nothing()
        {
            Feed(0.5f, Far);
            Feed(0.5f, Button, rightTracked: false);
            Assert.AreEqual(0, presses);
        }

        [Test]
        public void The_button_follows_the_wrist_along_the_eyes_up()
        {
            var tilted = Quaternion.Euler(0f, 0f, 30f);
            for (var i = 0; i < 40; i++)
            {
                var left = Left();
                left.EyeRotation = tilted;
                last = button.Step(left, Right(Far));
                time += Dt;
            }
            Assert.IsTrue(last.Shown);
            Assert.That(Vector3.Distance(last.Position, Wrist + tilted * Vector3.up * tuning.aboveWristMeters), Is.LessThan(1e-4f));
        }
    }
}
