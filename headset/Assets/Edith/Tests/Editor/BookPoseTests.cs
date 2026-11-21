using Edith.Hands;
using Edith.Scan;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class BookPoseTests
    {
        const float Dt = 0.02f;
        const float OpenCurl = 1.9f;
        static readonly Quaternion PalmUp = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        static readonly Quaternion PalmDown = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        static readonly Quaternion PalmRight = Quaternion.LookRotation(Vector3.right, Vector3.up);
        static readonly Quaternion PalmLeft = Quaternion.LookRotation(Vector3.left, Vector3.up);
        static readonly Vector3 LeftWrist = new Vector3(-0.1f, -0.3f, 0.35f);
        static readonly Vector3 RightWrist = new Vector3(0.1f, -0.3f, 0.35f);

        BookPose pose;
        float time;
        Pose eye;
        BookReading last;

        [SetUp]
        public void SetUp()
        {
            pose = new BookPose(new BookTuning());
            time = 0f;
            eye = Pose.identity;
            last = default;
        }

        /// A hand with its fingers along `turn`'s up, its wrist at `wrist`. Unturned, its palm faces +z. Each
        /// fingertip sits at `curl` times its knuckle's distance from the wrist. `sure` is false for a hand seen
        /// with low confidence in its thumb or index.
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

        /// Both palms up, the wrists `apart` metres apart about the middle of LeftWrist and RightWrist.
        static (HandFrame left, HandFrame right) Book(float apart = 0.2f, float curl = OpenCurl, float height = -0.3f,
            float ahead = 0.35f)
        {
            var half = new Vector3(apart * 0.5f, 0f, 0f);
            var middle = new Vector3(0f, height, ahead);
            return (Hand(middle - half, false, PalmUp, curl), Hand(middle + half, true, PalmUp, curl));
        }

        /// Steps the two hands for `seconds` and counts the times the book toggles.
        int Hold((HandFrame left, HandFrame right) hands, float seconds)
        {
            var toggles = 0;
            for (var end = time + seconds; time < end - 1e-4f;)
            {
                time += Dt;
                hands.left.Time = hands.right.Time = time;
                hands.left.EyePosition = hands.right.EyePosition = eye.position;
                hands.left.EyeRotation = hands.right.EyeRotation = eye.rotation;
                last = pose.Step(hands.left, hands.right);
                if (last.Toggled) toggles++;
            }
            return toggles;
        }

        static (HandFrame left, HandFrame right) Lost() => (default, default);

        [Test]
        public void Palms_up_side_by_side_open_once_after_half_a_second()
        {
            Assert.AreEqual(0, Hold(Book(), 0.4f), "under the hold time");
            Assert.AreEqual(1, Hold(Book(), 0.2f), "past the hold time");
            Assert.AreEqual(0, Hold(Book(), 5f), "one hold opens once, however long it lasts");
        }

        [Test]
        public void Progress_fills_over_the_hold_between_the_palms()
        {
            Hold(Book(), 0.26f);
            Assert.AreEqual(0.5f, last.Progress, 0.05f);
            Assert.AreEqual(0f, last.Between.x, 1e-4f);
            Assert.AreEqual(-0.3f, last.Between.y, 1e-4f);
            Hold(Book(), 0.3f);
            Assert.AreEqual(0f, last.Progress, "the ring is empty once the book has opened");
        }

        [Test]
        public void A_hold_that_ends_early_does_nothing_and_a_later_hold_opens()
        {
            Assert.AreEqual(0, Hold(Book(), 0.4f));
            Assert.AreEqual(0, Hold(Lost(), 0.5f));
            Assert.AreEqual(0, Hold(Book(), 0.4f), "the lost hands restarted the hold");
            Assert.AreEqual(1, Hold(Book(), 0.2f));
        }

        [Test]
        public void One_palm_down_does_not_open()
        {
            var (left, _) = Book();
            var right = Hand(RightWrist, true, PalmDown);
            Assert.AreEqual(0, Hold((left, right), 2f));
        }

        [Test]
        public void One_palm_turned_aside_does_not_open()
        {
            var (left, _) = Book();
            var right = Hand(RightWrist, true, Quaternion.LookRotation(Vector3.up + Vector3.left * 1.5f, Vector3.forward));
            Assert.AreEqual(0, Hold((left, right), 2f));
        }

        [Test]
        public void Hands_too_far_apart_or_too_close_do_not_open()
        {
            Assert.AreEqual(0, Hold(Book(apart: 0.5f), 2f), "wider than a book");
            Assert.AreEqual(0, Hold(Book(apart: 0.03f), 2f), "wrists touching");
            Assert.AreEqual(1, Hold(Book(apart: 0.3f), 0.6f), "a wide book still opens");
        }

        [Test]
        public void Hands_at_different_heights_do_not_open()
        {
            var (left, _) = Book();
            var right = Hand(RightWrist + Vector3.up * 0.15f, true, PalmUp);
            Assert.AreEqual(0, Hold((left, right), 2f));
        }

        [Test]
        public void Hands_above_the_eye_do_not_open()
        {
            Assert.AreEqual(0, Hold(Book(height: 0.1f), 2f));
        }

        [Test]
        public void Hands_too_near_or_too_far_ahead_do_not_open()
        {
            Assert.AreEqual(0, Hold(Book(ahead: 0.1f), 2f), "against the face");
            Assert.AreEqual(0, Hold(Book(ahead: 0.8f), 2f), "arms stretched");
            Assert.AreEqual(0, Hold(Book(ahead: -0.3f), 2f), "behind the user");
        }

        [Test]
        public void Curled_fists_do_not_open()
        {
            Assert.AreEqual(0, Hold(Book(curl: 1.0f), 2f));
        }

        [Test]
        public void Hands_seen_with_low_confidence_do_not_open()
        {
            var (left, right) = Book();
            left.Tracked = false;
            Assert.AreEqual(0, Hold((left, right), 2f));
        }

        [Test]
        public void A_blip_shorter_than_a_tenth_of_a_second_does_not_restart_the_hold()
        {
            Assert.AreEqual(0, Hold(Book(), 0.3f));
            Assert.AreEqual(0, Hold(Lost(), 0.06f));
            Assert.AreEqual(1, Hold(Book(), 0.3f), "the hold carried on through the blip");
        }

        [Test]
        public void A_blip_of_low_confidence_does_not_restart_the_hold()
        {
            var (left, right) = Book();
            left.Tracked = false;
            Assert.AreEqual(0, Hold(Book(), 0.3f));
            Assert.AreEqual(0, Hold((left, right), 0.06f));
            Assert.AreEqual(1, Hold(Book(), 0.3f));
        }

        [Test]
        public void A_loss_longer_than_the_grace_restarts_the_hold()
        {
            Assert.AreEqual(0, Hold(Book(), 0.3f));
            Assert.AreEqual(0, Hold(Lost(), 0.16f));
            Assert.AreEqual(0, Hold(Book(), 0.4f));
            Assert.AreEqual(1, Hold(Book(), 0.2f));
        }

        [Test]
        public void Each_hold_toggles_the_book_once_and_the_hands_must_leave_the_pose_between()
        {
            Assert.AreEqual(1, Hold(Book(), 0.6f), "opens");
            Assert.AreEqual(0, Hold(Book(), 3f), "a pose held on never flips it back");
            Assert.AreEqual(0, Hold(Lost(), 0.3f));
            Assert.AreEqual(1, Hold(Book(), 0.6f), "the same pose again closes");
            Assert.AreEqual(0, Hold(Lost(), 0.3f));
            Assert.AreEqual(1, Hold(Book(), 0.6f), "and opens again");
        }

        [Test]
        public void Lowering_the_hands_for_less_than_the_grace_does_not_count_as_leaving_the_pose()
        {
            Assert.AreEqual(1, Hold(Book(), 0.6f));
            Assert.AreEqual(0, Hold(Lost(), 0.06f));
            Assert.AreEqual(0, Hold(Book(), 1f));
        }

        [Test]
        public void A_hand_a_grab_claims_blocks_the_pose()
        {
            var (left, right) = Book();
            Assert.AreEqual(0, Hold((left, right.Unseen()), 2f));
            Assert.AreEqual(0, Hold((left.Unseen(), right), 2f));
        }

        [Test]
        public void Near_reports_a_pose_that_almost_passes()
        {
            Hold(Book(apart: 0.5f), 0.2f);
            Assert.IsTrue(last.Near);
            Hold(Book(curl: 1.0f), 0.2f);
            Assert.IsFalse(last.Near);
        }

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(70f)]
        public void The_open_pose_never_fires_scan(float lookDownDegrees)
        {
            var scan = new ScanPoseTracker(new ScanTuning());
            eye = new Pose(Vector3.zero, Quaternion.Euler(lookDownDegrees, 0f, 0f));
            var (left, right) = Book();
            var fired = 0;
            var scanned = 0f;
            for (var t = Dt; t < 3f; t += Dt)
            {
                left.Time = right.Time = t;
                left.EyePosition = right.EyePosition = eye.position;
                left.EyeRotation = right.EyeRotation = eye.rotation;
                var reading = scan.Step(left, right);
                if (reading.Fired) fired++;
                scanned = Mathf.Max(scanned, reading.Progress);
            }
            Assert.AreEqual(0, fired);
            Assert.AreEqual(0f, scanned, "the scan ring does not even start");
        }
    }
}
