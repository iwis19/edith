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

        /// dot(palm normal, world up) above this means a palm faces up: within 40 degrees.
        public float palmUpMin = 0.766f;
        /// GrabTracker.Curl above this is an open hand, the same value Scan reads.
        public float openCurl = 1.45f;
        /// The wrists sit this far apart on the horizontal plane, and within heightGapMax of each other's height.
        public float minApartMeters = 0.06f;
        public float maxApartMeters = 0.35f;
        public float heightGapMax = 0.10f;
        /// Each wrist is this far in front of the eye along its forward axis, and not above the eye.
        public float minAheadMeters = 0.20f;
        public float maxAheadMeters = 0.60f;
        public float holdSeconds = 0.5f;
        /// A hand lost, or out of the pose, for no longer than this does not restart a hold.
        public float graceSeconds = 0.10f;

        /// Palms face each other when dot(left palm normal, right palm normal) is below this.
        public float facingMax = -0.6f;
        /// Each palm normal must also point at least this much toward the other palm, so palms back to back do not close.
        public float towardMin = 0.5f;
        public float closeApartMeters = 0.07f;
        public float closeSeconds = 0.15f;
        /// After a close, the palms must part further than this before the next one.
        public float rearmApartMeters = 0.12f;

        /// Looser limits that only decide when a pose is close enough to log.
        public float nearPalmUp = 0.5f;
        public float nearCurl = 1.25f;
        public float nearCloseApartMeters = 0.15f;
        public float nearFacingMax = -0.3f;
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
        /// True on the one step a held open pose opens the book while it is closed.
        public readonly bool Opened;
        /// True on the one step the palms held together close the book while it is open.
        public readonly bool Closed;
        /// The pose the book wants next is close enough to log, whether or not it passes.
        public readonly bool Near;
        /// 0 out of the open pose, rising to 1 as the hold completes.
        public readonly float Progress;
        /// Halfway between the palms. Meaningful while Progress is above 0.
        public readonly Vector3 Between;
        public readonly BookRead Left, Right;
        /// The wrists' horizontal distance and height difference.
        public readonly float Apart, HeightGap;
        /// The palm points' distance, and dot(left palm normal, right palm normal).
        public readonly float PalmGap, Facing;

        public BookReading(bool opened, bool closed, bool near, float progress, Vector3 between, BookRead left, BookRead right,
            float apart, float heightGap, float palmGap, float facing)
        {
            Opened = opened;
            Closed = closed;
            Near = near;
            Progress = progress;
            Between = between;
            Left = left;
            Right = right;
            Apart = apart;
            HeightGap = heightGap;
            PalmGap = palmGap;
            Facing = facing;
        }

        public override string ToString() =>
            $"left {Left}, right {Right}, apart={Apart:F2} heightGap={HeightGap:F2} palmGap={PalmGap:F2} facing={Facing:F2}";
    }

    /// The pose that works the Scrapbook like a book. Open: both palms up side by side in front of the user, held.
    /// Close: both palms brought together facing each other. The open pose counts only while the book is closed
    /// and the close pose only while it is open; a hold that completes in the other state is spent, so the hands
    /// must leave it before it counts. A caller blanks a hand with HandFrame.Unseen while a grab claims it.
    /// Open needs HandFrame.Tracked, since joints read at low confidence can pass for a flat palm. Close needs
    /// only Visible, because hands pressed together lose that confidence. Deterministic in HandFrame.Time.
    public sealed class BookPose
    {
        sealed class Hold
        {
            readonly bool rearmsOnRelease;
            bool holding, fired;
            float since, lastPosed;

            public Hold(bool rearmsOnRelease) => this.rearmsOnRelease = rearmsOnRelease;

            public void Rearm() => fired = false;

            /// True on the one step a pose held for `seconds` completes.
            public bool Update(bool posed, float time, float seconds, float grace)
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
                else if (holding && time - lastPosed > grace)
                {
                    holding = false;
                    if (rearmsOnRelease) fired = false;
                }
                if (!posed || fired || time - since < seconds - 1e-4f) return false;
                fired = true;
                return true;
            }

            public float Progress(float time, float seconds) => holding && !fired ? Mathf.Clamp01((time - since) / seconds) : 0f;
        }

        readonly BookTuning tuning;
        readonly Hold open = new Hold(true), close = new Hold(false);

        public BookPose(BookTuning tuning) => this.tuning = tuning;

        /// `isOpen` is whether the book is open now.
        public BookReading Step(in HandFrame left, in HandFrame right, bool isOpen)
        {
            var time = right.Time;
            if (!left.Visible || !right.Visible)
            {
                open.Update(false, time, tuning.holdSeconds, tuning.graceSeconds);
                close.Update(false, time, tuning.closeSeconds, tuning.graceSeconds);
                return default;
            }

            var leftPalm = Palm(left);
            var rightPalm = Palm(right);
            var leftRead = Read(left);
            var rightRead = Read(right);
            var apart = Horizontal(right.Wrist - left.Wrist);
            var heightGap = Mathf.Abs(right.Wrist.y - left.Wrist.y);
            var palmGap = Vector3.Distance(leftPalm, rightPalm);
            var leftNormal = left.PalmNormal;
            var rightNormal = right.PalmNormal;
            var facing = Vector3.Dot(leftNormal, rightNormal);
            if (palmGap > tuning.rearmApartMeters) close.Rearm();

            var minUp = Mathf.Min(leftRead.Up, rightRead.Up);
            var minCurl = Mathf.Min(leftRead.Curl, rightRead.Curl);
            var placed = InFront(leftRead) && InFront(rightRead);
            var openPosed = left.Tracked && right.Tracked && placed && minUp > tuning.palmUpMin && minCurl > tuning.openCurl
                            && apart >= tuning.minApartMeters && apart <= tuning.maxApartMeters && heightGap <= tuning.heightGapMax;
            var toRight = palmGap < 1e-3f ? Vector3.zero : (rightPalm - leftPalm) / palmGap;
            var closePosed = facing < tuning.facingMax && palmGap <= tuning.closeApartMeters
                             && (palmGap < 0.01f || (Vector3.Dot(leftNormal, toRight) > tuning.towardMin
                                                     && Vector3.Dot(rightNormal, -toRight) > tuning.towardMin));

            if (!isOpen && !closePosed) close.Rearm();
            var opened = open.Update(openPosed, time, tuning.holdSeconds, tuning.graceSeconds) && !isOpen;
            var closed = close.Update(closePosed, time, tuning.closeSeconds, tuning.graceSeconds) && isOpen;
            var near = isOpen
                ? palmGap <= tuning.nearCloseApartMeters && facing < tuning.nearFacingMax
                : placed && minUp > tuning.nearPalmUp && minCurl > tuning.nearCurl;
            var progress = isOpen ? 0f : opened ? 1f : open.Progress(time, tuning.holdSeconds);
            return new BookReading(opened, closed, near, progress, (leftPalm + rightPalm) * 0.5f, leftRead, rightRead,
                apart, heightGap, palmGap, facing);
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
