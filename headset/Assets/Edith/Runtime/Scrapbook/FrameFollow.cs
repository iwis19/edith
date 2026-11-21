using UnityEngine;

namespace Edith.Scrapbook
{
    /// Keeps the Scrapbook in front of the user without making it chase the head: the deck stays put until the head
    /// has pointed well away from it, or the user has walked off, for a moment, and then eases back in front.
    public sealed class FrameFollow
    {
        /// The head must turn this far from the deck's heading, or move this far, before the deck follows.
        public const float OffDegrees = 20f, OffMeters = 0.35f;
        /// And stay that far off this long.
        public const float WaitSeconds = 0.5f;
        /// The rate of the ease back, per second.
        public const float Rate = 4f;

        float offFor;
        bool moving;

        /// The deck's frame after `seconds`, given where it is and where the eye is. Nothing moves while `busy`, such as
        /// during a pinch, so a target never slides away from the hand.
        public Pose Step(Pose frame, Pose eye, float seconds, bool busy)
        {
            if (busy)
            {
                offFor = 0f;
                moving = false;
                return frame;
            }
            var target = DeckLayout.Frame(eye);
            if (!moving)
            {
                var off = Quaternion.Angle(frame.rotation, target.rotation) > OffDegrees
                          || Vector3.Distance(frame.position, target.position) > OffMeters;
                offFor = off ? offFor + seconds : 0f;
                if (offFor < WaitSeconds) return frame;
                moving = true;
            }
            var t = 1f - Mathf.Exp(-Rate * seconds);
            var next = new Pose(Vector3.Lerp(frame.position, target.position, t), Quaternion.Slerp(frame.rotation, target.rotation, t));
            if (Quaternion.Angle(next.rotation, target.rotation) < 0.5f && Vector3.Distance(next.position, target.position) < 0.005f)
            {
                moving = false;
                offFor = 0f;
                return target;
            }
            return next;
        }
    }
}
