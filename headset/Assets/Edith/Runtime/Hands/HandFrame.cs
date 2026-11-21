using System;
using UnityEngine;

namespace Edith.Hands
{
    /// One frame of the right hand and the eye, in world space. Also the line format of hand recordings.
    [Serializable]
    public struct HandFrame
    {
        public float Time;
        public Vector3 EyePosition;
        public Quaternion EyeRotation;
        /// False when the hand is lost or tracking is unsure of the thumb or index.
        public bool Tracked;
        /// True whenever the hand is tracked at all, with any confidence. A fist usually fails Tracked's high
        /// confidence on the thumb and index, so grab reads this instead.
        public bool Visible;
        /// Horizon OS's index pinch flag.
        public bool Pinching;
        public Vector3 ThumbTip, IndexTip, Wrist, IndexKnuckle, LittleKnuckle;
        public Vector3 MiddleKnuckle, MiddleTip, RingKnuckle, RingTip;

        public Vector3 PinchPoint => (ThumbTip + IndexTip) * 0.5f;

        /// This frame with the hand unseen, so every gesture reads it as lost. The clock and the eye stay.
        public HandFrame Unseen()
        {
            var frame = this;
            frame.Tracked = frame.Visible = frame.Pinching = false;
            return frame;
        }

        /// The frame as gestures may read it: unseen while the pairing gate is closed.
        public HandFrame Gated(bool open) => open ? this : Unseen();
        public Pose Eye => new Pose(EyePosition, EyeRotation);

        /// Points out of the palm. The raw cross product points into a right palm, so it is flipped.
        public Vector3 PalmNormal => -Vector3.Cross(IndexKnuckle - Wrist, LittleKnuckle - Wrist).normalized;
    }
}
