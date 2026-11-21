using Edith.Grab;
using Edith.Hands;
using UnityEngine;

namespace Edith.Scan
{
    /// What one hand reads against the pose, worth logging when the pose is near.
    public readonly struct PalmRead
    {
        /// dot(palm normal, the line from the eye to the palm).
        public readonly float Away;
        /// dot(palm normal, straight down). 1 is a palm flat toward the floor.
        public readonly float Down;
        /// dot(palm normal, where the head points). 1 is a palm facing the way the user looks.
        public readonly float Along;
        public readonly float Curl;

        public PalmRead(float away, float down, float along, float curl)
        {
            Away = away;
            Down = down;
            Along = along;
            Curl = curl;
        }

        /// A palm toward the floor is a hand typing or resting, unless it also faces where the user looks down.
        public bool FloorFacing(ScanTuning tuning) => Down > tuning.palmDownMax && Along < tuning.palmAlongLookMin;

        public override string ToString() => $"away={Away:F2} down={Down:F2} along={Along:F2} curl={Curl:F2}";
    }

    public readonly struct ScanPoseReading
    {
        /// True on the one step the held pose becomes a scan.
        public readonly bool Fired;
        /// Both hands are up and roughly in the pose, whether or not they pass.
        public readonly bool Near;
        /// 0 out of the pose, rising to 1 as the hold completes.
        public readonly float Progress;
        /// Halfway between the palms. Meaningful while Progress is above 0.
        public readonly Vector3 Between;
        public readonly PalmRead Left, Right;

        public ScanPoseReading(bool fired, bool near, float progress, Vector3 between, PalmRead left, PalmRead right)
        {
            Fired = fired;
            Near = near;
            Progress = progress;
            Between = between;
            Left = left;
            Right = right;
        }
    }

    /// The Scan pose: both hands open, palms facing away from the user and not at the floor unless the user looks
    /// down the same way, held in front of them.
    /// A hand counts only while HandFrame.Tracked, since joints read at low confidence can pass for an open palm.
    /// Fires once per hold. Deterministic in HandFrame.Time.
    public sealed class ScanPoseTracker
    {
        readonly ScanTuning tuning;
        bool holding, fired;
        float heldSince, lastPosed;
        float readyAt = float.NegativeInfinity;

        public ScanPoseTracker(ScanTuning tuning) => this.tuning = tuning;

        public ScanPoseReading Step(in HandFrame left, in HandFrame right)
        {
            var time = right.Time;
            if (!left.Tracked || !right.Tracked)
            {
                Release(time);
                return default;
            }

            var leftPalm = Palm(left);
            var rightPalm = Palm(right);
            var leftRead = Read(left, leftPalm, left.PalmNormal);
            var rightRead = Read(right, rightPalm, right.PalmNormal);
            var ahead = Ahead(right, leftPalm) && Ahead(right, rightPalm);
            var posed = ahead && Mathf.Min(leftRead.Away, rightRead.Away) > tuning.palmAwayMin
                              && !leftRead.FloorFacing(tuning) && !rightRead.FloorFacing(tuning)
                              && Mathf.Min(leftRead.Curl, rightRead.Curl) > tuning.openCurl;
            var near = ahead && Mathf.Min(leftRead.Away, rightRead.Away) > tuning.nearPalmAway
                             && Mathf.Min(leftRead.Curl, rightRead.Curl) > tuning.nearCurl;

            if (posed)
            {
                if (!holding)
                {
                    holding = true;
                    heldSince = time;
                }
                lastPosed = time;
            }
            else
            {
                Release(time);
            }

            var held = holding ? time - heldSince : 0f;
            var fire = posed && !fired && held >= tuning.holdSeconds && time >= readyAt;
            if (fire)
            {
                fired = true;
                readyAt = time + tuning.cooldownSeconds;
            }
            // The ring stays empty after a scan until the hands leave the pose, since holding on fires nothing more.
            var progress = holding && !fired ? Mathf.Clamp01(held / tuning.holdSeconds) : 0f;
            return new ScanPoseReading(fire, near, fire ? 1f : progress, (leftPalm + rightPalm) * 0.5f, leftRead, rightRead);
        }

        Vector3 Palm(in HandFrame hand) => Vector3.Lerp(hand.Wrist, hand.MiddleKnuckle, tuning.palmAlong);

        static PalmRead Read(in HandFrame hand, Vector3 palm, Vector3 normal) =>
            new PalmRead(Vector3.Dot(normal, (palm - hand.EyePosition).normalized), Vector3.Dot(normal, Vector3.down),
                Vector3.Dot(normal, hand.EyeRotation * Vector3.forward), GrabTracker.Curl(hand));

        bool Ahead(in HandFrame eye, Vector3 palm)
        {
            var toPalm = palm - eye.EyePosition;
            return Vector3.Dot(toPalm, eye.EyeRotation * Vector3.forward) >= tuning.minAheadMeters && -toPalm.y <= tuning.maxBelowMeters;
        }

        void Release(float time)
        {
            if (!holding || time - lastPosed <= tuning.graceSeconds) return;
            holding = false;
            fired = false;
        }
    }
}
