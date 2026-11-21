using Edith.Hands;
using Edith.Scan;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class ScanPoseTrackerTests
    {
        const float Dt = 0.02f;
        const float OpenCurl = 1.9f;
        static readonly Vector3 LeftWrist = new Vector3(-0.15f, -0.15f, 0.35f);
        static readonly Vector3 RightWrist = new Vector3(0.15f, -0.15f, 0.35f);
        static readonly Quaternion Away = Quaternion.identity;
        static readonly Quaternion TowardUser = Quaternion.AngleAxis(180f, Vector3.up);
        static readonly Quaternion TowardFloor = Quaternion.AngleAxis(90f, Vector3.right);

        ScanPoseTracker tracker;
        float time;
        Pose eye;
        ScanPoseReading last;

        [SetUp]
        public void SetUp()
        {
            tracker = new ScanPoseTracker(new ScanTuning());
            time = 0f;
            eye = Pose.identity;
            last = default;
        }

        /// A hand with its fingers up and its wrist at `wrist`, turned by `turn` about the wrist. Unturned, its palm
        /// faces +z. Each fingertip sits at `curl` times its knuckle's distance from the wrist. `sure` is false for a
        /// hand seen with low confidence in its thumb or index.
        static HandFrame Hand(Vector3 wrist, bool right, Quaternion turn, float curl = OpenCurl, bool visible = true,
            bool sure = true)
        {
            var side = right ? 1f : -1f;
            Vector3 Knuckle(float x, float y) => wrist + turn * new Vector3(x * side, y, 0f);
            var index = Knuckle(-0.03f, 0.085f);
            var middle = Knuckle(0f, 0.09f);
            var ring = Knuckle(0.02f, 0.085f);
            return new HandFrame
            {
                Side = right ? HandSide.Right : HandSide.Left,
                Visible = visible,
                Tracked = visible && sure,
                Wrist = wrist,
                IndexKnuckle = index,
                IndexTip = wrist + (index - wrist) * curl,
                MiddleKnuckle = middle,
                MiddleTip = wrist + (middle - wrist) * curl,
                RingKnuckle = ring,
                RingTip = wrist + (ring - wrist) * curl,
                LittleKnuckle = Knuckle(0.04f, 0.07f),
            };
        }

        static (HandFrame left, HandFrame right) PalmsOut() => (Hand(LeftWrist, false, Away), Hand(RightWrist, true, Away));

        /// Steps the two hands for `seconds` and returns how many scans fired.
        int Hold((HandFrame left, HandFrame right) hands, float seconds)
        {
            var fired = 0;
            for (var end = time + seconds; time < end - 1e-4f;)
            {
                time += Dt;
                hands.left.Time = hands.right.Time = time;
                hands.left.EyePosition = hands.right.EyePosition = eye.position;
                hands.left.EyeRotation = hands.right.EyeRotation = eye.rotation;
                last = tracker.Step(hands.left, hands.right);
                if (last.Fired) fired++;
            }
            return fired;
        }

        [Test]
        public void Open_palms_facing_away_fire_once_after_half_a_second()
        {
            Assert.AreEqual(0, Hold(PalmsOut(), 0.4f), "under the hold time");
            Assert.AreEqual(1, Hold(PalmsOut(), 0.2f), "past the hold time");
            Assert.AreEqual(0, Hold(PalmsOut(), 5f), "one hold is one scan, however long it lasts");
        }

        [Test]
        public void Progress_fills_over_the_hold_between_the_palms()
        {
            Hold(PalmsOut(), 0.26f);
            Assert.AreEqual(0.5f, last.Progress, 0.05f);
            Assert.AreEqual(0f, last.Between.x, 1e-4f);
            Assert.AreEqual(0.35f, last.Between.z, 1e-4f);
            Hold(PalmsOut(), 0.1f);
            Assert.Greater(last.Progress, 0.6f);
            Assert.Less(last.Progress, 1f);
        }

        [Test]
        public void Progress_is_full_on_the_firing_step_and_empty_while_the_hold_goes_on()
        {
            var fired = false;
            for (var i = 0; i < 40 && !fired; i++)
            {
                Hold(PalmsOut(), Dt);
                fired = last.Fired;
            }
            Assert.IsTrue(fired);
            Assert.AreEqual(1f, last.Progress);
            Hold(PalmsOut(), 0.2f);
            Assert.AreEqual(0f, last.Progress);
        }

        [Test]
        public void Palms_facing_the_user_never_fire()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, TowardUser), Hand(RightWrist, true, TowardUser)), 2f));
            Assert.AreEqual(0f, last.Progress);
        }

        [Test]
        public void One_palm_facing_the_user_never_fires()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away), Hand(RightWrist, true, TowardUser)), 2f));
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, TowardUser), Hand(RightWrist, true, Away)), 2f));
        }

        [Test]
        public void Palms_facing_the_floor_never_fire()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, TowardFloor), Hand(RightWrist, true, TowardFloor)), 2f));
        }

        [Test]
        public void Palms_held_out_while_looking_down_fire()
        {
            var down = Quaternion.AngleAxis(60f, Vector3.right);
            eye = new Pose(Vector3.zero, down);
            Assert.AreEqual(1, Hold((Hand(down * LeftWrist, false, down), Hand(down * RightWrist, true, down)), 0.6f));
            Assert.Greater(last.Left.Down, 0.7f, "the palms face the floor");
        }

        [Test]
        public void Hands_on_a_keyboard_the_user_glances_at_never_fire()
        {
            eye = new Pose(Vector3.zero, Quaternion.AngleAxis(35f, Vector3.right));
            var typing = (Hand(new Vector3(-0.15f, -0.4f, 0.35f), false, TowardFloor), Hand(new Vector3(0.15f, -0.4f, 0.35f), true, TowardFloor));
            Assert.AreEqual(0, Hold(typing, 2f));
            Assert.Greater(last.Left.Away, 0.5f, "only the floor-facing check keeps these out");
        }

        [Test]
        public void A_fist_never_fires()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away), Hand(RightWrist, true, Away, curl: 1f)), 2f));
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away, curl: 1f), Hand(RightWrist, true, Away)), 2f));
        }

        [Test]
        public void One_hand_never_fires()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away, visible: false), Hand(RightWrist, true, Away)), 2f));
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away), Hand(RightWrist, true, Away, visible: false)), 2f));
        }

        [Test]
        public void A_hand_unsure_of_its_thumb_or_index_never_fires()
        {
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away, sure: false), Hand(RightWrist, true, Away)), 2f));
            Assert.AreEqual(0, Hold((Hand(LeftWrist, false, Away), Hand(RightWrist, true, Away, sure: false)), 2f));
            Assert.AreEqual(0f, last.Progress);
        }

        [Test]
        public void Hands_beside_the_head_never_fire()
        {
            var hands = (Hand(new Vector3(-0.25f, -0.1f, 0.02f), false, Away), Hand(new Vector3(0.25f, -0.1f, 0.02f), true, Away));
            Assert.AreEqual(0, Hold(hands, 2f));
        }

        [Test]
        public void Hands_hanging_at_the_sides_never_fire()
        {
            var hands = (Hand(new Vector3(-0.2f, -0.65f, 0.15f), false, Away), Hand(new Vector3(0.2f, -0.65f, 0.15f), true, Away));
            Assert.AreEqual(0, Hold(hands, 2f));
        }

        [Test]
        public void The_pose_follows_where_the_user_faces()
        {
            var turn = Quaternion.AngleAxis(90f, Vector3.up);
            eye = new Pose(Vector3.zero, turn);
            Assert.AreEqual(0, Hold(PalmsOut(), 2f), "hands left where the user no longer faces");
            var round = (Hand(turn * LeftWrist, false, turn), Hand(turn * RightWrist, true, turn));
            Assert.AreEqual(1, Hold(round, 0.6f), "hands brought round with the head");
        }

        [Test]
        public void Dropping_the_pose_partway_starts_the_hold_again()
        {
            var dropped = (Hand(LeftWrist, false, Away), Hand(RightWrist, true, Away, visible: false));
            Assert.AreEqual(0, Hold(PalmsOut(), 0.4f));
            Assert.AreEqual(0, Hold(dropped, 0.3f));
            Assert.AreEqual(0, Hold(PalmsOut(), 0.4f), "the earlier 0.4 s does not count");
            Assert.AreEqual(1, Hold(PalmsOut(), 0.2f));
        }

        [Test]
        public void A_tracking_blink_does_not_restart_the_hold()
        {
            var blink = (Hand(LeftWrist, false, Away, visible: false), Hand(RightWrist, true, Away));
            Assert.AreEqual(0, Hold(PalmsOut(), 0.3f));
            Assert.AreEqual(0, Hold(blink, 0.06f));
            Assert.AreEqual(1, Hold(PalmsOut(), 0.2f));
        }

        [Test]
        public void A_second_scan_waits_for_the_cooldown_and_for_the_hands_to_leave_the_pose()
        {
            var down = (Hand(LeftWrist, false, Away, visible: false), Hand(RightWrist, true, Away, visible: false));
            Assert.AreEqual(1, Hold(PalmsOut(), 0.6f));
            Assert.AreEqual(0, Hold(down, 0.3f));
            Assert.AreEqual(0, Hold(PalmsOut(), 1.5f), "posed again, but within 2 s of the last scan");
            Assert.AreEqual(1, Hold(PalmsOut(), 0.3f), "the cooldown has passed with the pose still held");
            Assert.AreEqual(0, Hold(down, 0.3f));
            Assert.AreEqual(0, Hold(PalmsOut(), 0.4f));
        }

        [Test]
        public void A_near_miss_reads_as_near_with_each_hands_values()
        {
            var halfOpen = (Hand(LeftWrist, false, Away, curl: 1.35f), Hand(RightWrist, true, Away));
            Assert.AreEqual(0, Hold(halfOpen, 1f));
            Assert.IsTrue(last.Near);
            Assert.AreEqual(1.35f, last.Left.Curl, 0.01f);
            Assert.Greater(last.Right.Away, 0.5f);
            Assert.Greater(last.Left.Away, 0.5f);
        }
    }
}
