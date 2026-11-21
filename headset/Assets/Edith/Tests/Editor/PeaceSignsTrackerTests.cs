using Edith.Hands;
using Edith.Scan;
using Edith.Welcome;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class PeaceSignsTrackerTests
    {
        const float Dt = 0.02f;
        const float Out = 1.9f, Curled = 1.1f;
        static readonly Vector3 LeftWrist = new Vector3(-0.15f, -0.15f, 0.35f);
        static readonly Vector3 RightWrist = new Vector3(0.15f, -0.15f, 0.35f);

        PeaceSignsTracker tracker;
        float time;

        [SetUp]
        public void SetUp()
        {
            tracker = new PeaceSignsTracker();
            time = 0f;
        }

        /// A hand with its fingers up and its palm facing away from the eye, each fingertip at its own multiple of its
        /// knuckle's distance from the wrist.
        static HandFrame Hand(Vector3 wrist, bool right, float index, float middle, float ring, bool sure = true)
        {
            var side = right ? 1f : -1f;
            Vector3 Knuckle(float x, float y) => wrist + new Vector3(x * side, y, 0f);
            var indexKnuckle = Knuckle(-0.03f, 0.085f);
            var middleKnuckle = Knuckle(0f, 0.09f);
            var ringKnuckle = Knuckle(0.02f, 0.085f);
            return new HandFrame
            {
                Side = right ? HandSide.Right : HandSide.Left,
                Visible = true,
                Tracked = sure,
                Wrist = wrist,
                IndexKnuckle = indexKnuckle,
                IndexTip = wrist + (indexKnuckle - wrist) * index,
                MiddleKnuckle = middleKnuckle,
                MiddleTip = wrist + (middleKnuckle - wrist) * middle,
                RingKnuckle = ringKnuckle,
                RingTip = wrist + (ringKnuckle - wrist) * ring,
                LittleKnuckle = Knuckle(0.04f, 0.07f),
                EyeRotation = Quaternion.identity,
            };
        }

        static (HandFrame, HandFrame) Both(float index, float middle, float ring) =>
            (Hand(LeftWrist, false, index, middle, ring), Hand(RightWrist, true, index, middle, ring));

        static (HandFrame, HandFrame) Peace() => Both(Out, Out, Curled);

        int Hold((HandFrame left, HandFrame right) hands, float seconds)
        {
            var fired = 0;
            for (var end = time + seconds; time < end - 1e-4f;)
            {
                time += Dt;
                hands.left.Time = hands.right.Time = time;
                if (tracker.Step(hands.left, hands.right)) fired++;
            }
            return fired;
        }

        [Test]
        public void Two_peace_signs_held_fire_once()
        {
            Assert.AreEqual(0, Hold(Peace(), PeaceSignsTracker.HoldSeconds - 0.1f), "not before the hold is up");
            Assert.AreEqual(1, Hold(Peace(), 3f), "once, however long it is held");
        }

        [Test]
        public void Dropping_the_sign_lets_it_fire_again()
        {
            Assert.AreEqual(1, Hold(Peace(), 2f));
            Hold(Both(Out, Out, Out), 0.5f);
            Assert.AreEqual(1, Hold(Peace(), 2f));
        }

        [Test]
        public void A_tracking_blink_does_not_restart_the_hold()
        {
            Hold(Peace(), 1f);
            var (left, right) = Peace();
            left.Tracked = false;
            Hold((left, right), 0.1f);
            Assert.AreEqual(1, Hold(Peace(), 0.6f));
        }

        [Test]
        public void Other_hand_shapes_never_fire()
        {
            var (left, _) = Peace();
            Assert.AreEqual(0, Hold((left, Hand(RightWrist, true, Out, Out, Out)), 3f), "one peace sign and an open hand");
            Assert.AreEqual(0, Hold(Both(Out, Out, Out), 3f), "open hands");
            Assert.AreEqual(0, Hold(Both(Out, Curled, Curled), 3f), "finger guns");
            Assert.AreEqual(0, Hold(Both(Curled, Curled, Curled), 3f), "fists");
        }

        [Test]
        public void Peace_signs_do_not_fire_a_scan()
        {
            var scan = new ScanPoseTracker(new ScanTuning());
            var (left, right) = Peace();
            var fired = false;
            for (var t = Dt; t < 4f; t += Dt)
            {
                left.Time = right.Time = t;
                fired |= scan.Step(left, right).Fired;
            }
            Assert.IsFalse(fired);
        }
    }
}
