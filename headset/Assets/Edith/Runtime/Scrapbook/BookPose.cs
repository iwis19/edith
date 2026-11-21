using System;
using Edith.Grab;
using Edith.Hands;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// Book pose thresholds. Distances are metres.
    [Serializable]
    public sealed class BookTuning
    {
        /// The palm point, as a fraction of the way from the wrist to the middle knuckle.
        public float palmAlong = 0.5f;

        /// dot(palm normal, world up) above this means a palm faces up: within 50 degrees.
        public float palmUpMin = 0.643f;
        /// GrabTracker.Curl above this is an open hand, the same value Scan reads.
        public float openCurl = 1.45f;
        /// The wrists sit this far apart on the horizontal plane, and within heightGapMax of each other's height.
        public float minApartMeters = 0.06f;
        public float maxApartMeters = 0.35f;
        public float heightGapMax = 0.10f;
        /// Each wrist is this far in front of the eye along its forward axis, and not above the eye.
        public float minAheadMeters = 0.12f;
        public float maxAheadMeters = 0.60f;
        public float holdSeconds = 0.5f;
        /// A hand lost, or out of the pose, for no longer than this does not restart a hold. Palms up, the headset
        /// sees each hand from its palm side and loses it for a few frames now and then.
        public float graceSeconds = 0.25f;

        /// Looser limits that only decide when a pose is close enough to log.
        public float nearPalmUp = 0.5f;
        public float nearCurl = 1.25f;
    }

    /// What one hand reads against the open pose, worth logging when the pose is near.
    public readonly struct BookRead
    {
        /// dot(palm normal, world up). 1 is a palm flat toward the ceiling.
        public readonly float Up;
        public readonly float Curl;
        /// The wrist's distance in front of the eye along the eye's forward axis.
        public readonly float Ahead;
        /// How far the wrist sits below the eye.
        public readonly float Below;

        public BookRead(float up, float curl, float ahead, float below)
        {
            Up = up;
            Curl = curl;
            Ahead = ahead;
            Below = below;
        }

        public override string ToString() => $"up={Up:F2} curl={Curl:F2} ahead={Ahead:F2} below={Below:F2}";
    }

    public readonly struct BookReading
    {
        /// True on the one step a held pose opens the book when it is closed, or closes it when it is open.
        public readonly bool Toggled;
        /// The pose is close enough to log, whether or not it passes.
        public readonly bool Near;
        /// How far the hold has filled, 0 to 1.
        public readonly float Progress;
        /// Between the palms, where the ring shows.
        public readonly Vector3 Between;
        public readonly BookRead Left, Right;
        /// The wrists' horizontal distance and their height difference, in metres.
        public readonly float Apart, HeightGap;

        public BookReading(bool toggled, bool near, float progress, Vector3 between, BookRead left, BookRead right,
            float apart, float heightGap)
        {
            Toggled = toggled;
            Near = near;
            Progress = progress;
            Between = between;
            Left = left;
            Right = right;
            Apart = apart;
            HeightGap = heightGap;
        }

        public override string ToString() => $"left {Left}, right {Right}, apart={Apart:F2} heightGap={HeightGap:F2}";
    }

    /// The pose that opens and closes the Scrapbook like a book: both palms up side by side in front of the user,
    /// held. Each completed hold toggles the book, and the hands must leave the pose before the next one counts, so
    /// a held pose never flips it back. A caller blanks a hand with HandFrame.Unseen while a grab claims it. Needs
    /// HandFrame.Tracked to start a hold, since joints read at low confidence can pass for a flat palm; once a hold
    /// is under way, a hand seen with low confidence in the same pose keeps it going. Deterministic in
    /// HandFrame.Time.
    public sealed class BookPose
    {
        readonly BookTuning tuning;
        bool holding, fired;
        float since, lastPosed;

        public BookPose(BookTuning tuning) => this.tuning = tuning;

        public BookReading Step(in HandFrame left, in HandFrame right)
        {
            var time = right.Time;
            if (!left.Visible || !right.Visible)
            {
                Update(false, time);
                return default;
            }

            var leftPalm = Palm(left);
            var rightPalm = Palm(right);
            var leftRead = Read(left);
            var rightRead = Read(right);
            var apart = Horizontal(right.Wrist - left.Wrist);
            var heightGap = Mathf.Abs(right.Wrist.y - left.Wrist.y);
            var minUp = Mathf.Min(leftRead.Up, rightRead.Up);
            var minCurl = Mathf.Min(leftRead.Curl, rightRead.Curl);
            var placed = InFront(leftRead) && InFront(rightRead);
            var sure = holding || left.Tracked && right.Tracked;
            var posed = sure && placed && minUp > tuning.palmUpMin && minCurl > tuning.openCurl
                        && apart >= tuning.minApartMeters && apart <= tuning.maxApartMeters && heightGap <= tuning.heightGapMax;

            var toggled = Update(posed, time);
            var near = placed && minUp > tuning.nearPalmUp && minCurl > tuning.nearCurl;
            var progress = toggled ? 1f : holding && !fired ? Mathf.Clamp01((time - since) / tuning.holdSeconds) : 0f;
            return new BookReading(toggled, near, progress, (leftPalm + rightPalm) * 0.5f, leftRead, rightRead, apart, heightGap);
        }

        /// True on the one step a pose held for the hold time completes. A gap no longer than the grace does not
        /// restart the hold; a longer one ends it and lets the next hold count.
        bool Update(bool posed, float time)
        {
            if (posed)
            {
                if (!holding)
                {
                    holding = true;
                    since = time;
                }
                lastPosed = time;
            }
            else if (holding && time - lastPosed > tuning.graceSeconds)
            {
                holding = false;
                fired = false;
            }
            if (!posed || fired || time - since < tuning.holdSeconds - 1e-4f) return false;
            fired = true;
            return true;
        }

        Vector3 Palm(in HandFrame hand) => Vector3.Lerp(hand.Wrist, hand.MiddleKnuckle, tuning.palmAlong);

        static BookRead Read(in HandFrame hand)
        {
            var toWrist = hand.Wrist - hand.EyePosition;
            return new BookRead(Vector3.Dot(hand.PalmNormal, Vector3.up), GrabTracker.Curl(hand),
                Vector3.Dot(toWrist, hand.EyeRotation * Vector3.forward), -toWrist.y);
        }

        bool InFront(in BookRead read) =>
            read.Ahead >= tuning.minAheadMeters && read.Ahead <= tuning.maxAheadMeters && read.Below >= 0f;

        static float Horizontal(Vector3 offset) => Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
    }
}
