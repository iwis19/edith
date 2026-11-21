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
        /// Horizon OS's index pinch flag.
        public bool Pinching;
        public Vector3 ThumbTip, IndexTip, Wrist, IndexKnuckle, LittleKnuckle;

        public Vector3 PinchPoint => (ThumbTip + IndexTip) * 0.5f;
        public float FingerGap => Vector3.Distance(ThumbTip, IndexTip);
        public Pose Eye => new Pose(EyePosition, EyeRotation);

        /// Points out of the palm. The raw cross product points into a right palm, so it is flipped.
        public Vector3 PalmNormal => -Vector3.Cross(IndexKnuckle - Wrist, LittleKnuckle - Wrist).normalized;
    }
}
