using Edith.Diagnostics;
using Edith.Grab;
using Edith.Hands;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PanelToggleTests
    {
        const float Dt = 1f / 72f;
        const float Open = 1.9f;
        const float Relaxed = 1.3f;
        const float Fist = 1.0f;

        PanelToggle toggle;
        float time;
        int toggles;

        [SetUp]
        public void SetUp()
        {
            toggle = new PanelToggle(new GrabTuning());
            time = 0f;
            toggles = 0;
        }

        static HandFrame Hand(float t, float curl, bool visible = true)
        {
            var index = new Vector3(-0.03f, 0.085f, 0f);
            var middle = new Vector3(0f, 0.09f, 0f);
            var ring = new Vector3(0.02f, 0.085f, 0f);
            return new HandFrame
            {
                Time = t,
                Visible = visible,
                Wrist = Vector3.zero,
                IndexKnuckle = index,
                IndexTip = index * curl,
                MiddleKnuckle = middle,
                MiddleTip = middle * curl,
                RingKnuckle = ring,
                RingTip = ring * curl,
            };
        }

        void Feed(float left, float right, float seconds, bool leftVisible = true, bool grabbing = false)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                if (toggle.Step(Hand(time, left, leftVisible), Hand(time, right), grabbing)) toggles++;
                time += Dt;
            }
        }

        [Test]
        public void Both_fists_held_for_a_second_toggle_once()
        {
            Feed(Open, Open, 0.5f);
            Feed(Fist, Fist, PanelToggle.HoldSeconds + 0.5f);

            Assert.AreEqual(1, toggles);
        }

        [Test]
        public void One_fist_alone_toggles_nothing()
        {
            Feed(Fist, Open, 2f);
            Feed(Open, Fist, 2f);
            Feed(Fist, Relaxed, 2f);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void The_hold_counts_from_when_the_second_fist_closes()
        {
            Feed(Fist, Open, 0.8f);
            Feed(Fist, Fist, 0.8f);
            Assert.AreEqual(0, toggles);

            Feed(Fist, Fist, 0.3f);

            Assert.AreEqual(1, toggles);
        }

        [Test]
        public void A_fist_that_grabbed_a_card_toggles_nothing()
        {
            Feed(Open, Open, 0.3f);
            Feed(Fist, Fist, 2f, grabbing: true);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void Fists_that_open_early_toggle_nothing()
        {
            Feed(Open, Open, 0.5f);
            Feed(Fist, Fist, 0.7f);
            Feed(Open, Fist, 0.5f);
            Feed(Fist, Fist, 0.7f);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void A_hand_must_open_before_the_next_hold_counts()
        {
            Feed(Fist, Fist, PanelToggle.HoldSeconds + 0.2f);
            Feed(Relaxed, Relaxed, 0.5f);
            Feed(Fist, Fist, PanelToggle.HoldSeconds + 0.2f);
            Assert.AreEqual(1, toggles);

            Feed(Fist, Open, 0.3f);
            Feed(Fist, Fist, PanelToggle.HoldSeconds + 0.2f);

            Assert.AreEqual(2, toggles);
        }

        [Test]
        public void Losing_a_hand_restarts_the_hold()
        {
            Feed(Fist, Fist, 0.7f);
            Feed(Fist, Fist, 0.5f, leftVisible: false);
            Feed(Fist, Fist, 0.7f);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void Relaxed_hands_between_the_thresholds_do_not_start_a_hold()
        {
            Feed(Open, Open, 0.3f);
            Feed(Relaxed, Relaxed, 3f);

            Assert.AreEqual(0, toggles);
        }
    }
}
