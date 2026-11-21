using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class FrameFollowTests
    {
        const float Dt = 1f / 72f;
        static readonly Pose Ahead = new Pose(Vector3.zero, Quaternion.identity);

        static Pose Turned(float degrees) => new Pose(Vector3.zero, Quaternion.Euler(0f, degrees, 0f));

        static Pose Run(FrameFollow follow, Pose frame, Pose eye, float seconds, bool busy = false)
        {
            for (var t = 0f; t < seconds; t += Dt) frame = follow.Step(frame, eye, Dt, busy);
            return frame;
        }

        [Test]
        public void A_glance_inside_the_margin_leaves_the_deck_where_it_is()
        {
            var frame = Run(new FrameFollow(), Ahead, Turned(15f), 3f);
            Assert.AreEqual(0f, Quaternion.Angle(frame.rotation, Ahead.rotation), 1e-3f);
        }

        [Test]
        public void A_quick_look_away_leaves_the_deck_where_it_is()
        {
            var follow = new FrameFollow();
            var frame = Run(follow, Ahead, Turned(40f), 0.3f);
            frame = Run(follow, frame, Ahead, 2f);
            Assert.AreEqual(0f, Quaternion.Angle(frame.rotation, Ahead.rotation), 1e-3f);
        }

        [Test]
        public void A_head_turned_well_away_for_a_moment_brings_the_deck_back_in_front()
        {
            var frame = Run(new FrameFollow(), Ahead, Turned(35f), 3f);
            Assert.AreEqual(0f, Quaternion.Angle(frame.rotation, Turned(35f).rotation), 0.5f);
        }

        [Test]
        public void Looking_down_at_the_hands_does_not_count_as_turning_away()
        {
            var down = new Pose(Vector3.zero, Quaternion.Euler(50f, 0f, 0f));
            var frame = Run(new FrameFollow(), Ahead, down, 3f);
            Assert.AreEqual(0f, Quaternion.Angle(frame.rotation, Ahead.rotation), 1e-3f);
        }

        [Test]
        public void Nothing_moves_during_a_pinch()
        {
            var frame = Run(new FrameFollow(), Ahead, Turned(60f), 3f, busy: true);
            Assert.AreEqual(0f, Quaternion.Angle(frame.rotation, Ahead.rotation), 1e-3f);
        }

        [Test]
        public void Walking_off_brings_the_deck_along()
        {
            var moved = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);
            var frame = Run(new FrameFollow(), Ahead, moved, 3f);
            Assert.AreEqual(1f, frame.position.z, 0.01f);
        }
    }
}
