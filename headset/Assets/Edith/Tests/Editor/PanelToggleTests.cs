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

        void Feed(float curl, float seconds, bool visible = true)
        {
            for (var elapsed = 0f; elapsed < seconds - 1e-5f; elapsed += Dt)
            {
                if (toggle.Step(Hand(time, curl, visible))) toggles++;
                time += Dt;
            }
        }

        [Test]
        public void A_fist_held_for_a_second_toggles_once()
        {
            Feed(Open, 0.5f);
            Feed(Fist, PanelToggle.HoldSeconds + 0.5f);

            Assert.AreEqual(1, toggles);
        }

        [Test]
        public void A_fist_that_opens_early_toggles_nothing()
        {
            Feed(Open, 0.5f);
            Feed(Fist, 0.7f);
            Feed(Open, 0.5f);
            Feed(Fist, 0.7f);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void The_hand_must_open_before_the_next_hold_counts()
        {
            Feed(Fist, PanelToggle.HoldSeconds + 0.2f);
            Feed(Relaxed, 0.5f);
            Feed(Fist, PanelToggle.HoldSeconds + 0.2f);
            Assert.AreEqual(1, toggles);

            Feed(Open, 0.3f);
            Feed(Fist, PanelToggle.HoldSeconds + 0.2f);

            Assert.AreEqual(2, toggles);
        }

        [Test]
        public void Losing_the_hand_restarts_the_hold()
        {
            Feed(Fist, 0.7f);
            Feed(Fist, 0.5f, visible: false);
            Feed(Fist, 0.7f);

            Assert.AreEqual(0, toggles);
        }

        [Test]
        public void A_relaxed_hand_between_the_thresholds_does_not_start_a_hold()
        {
            Feed(Open, 0.3f);
            Feed(Relaxed, 3f);

            Assert.AreEqual(0, toggles);
        }
    }
}
