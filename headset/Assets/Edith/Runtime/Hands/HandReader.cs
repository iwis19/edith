using UnityEngine;

namespace Edith.Hands
{
    /// Reads one tracked hand and the eye into a HandFrame.
    public sealed class HandReader
    {
        readonly HandSide side;
        readonly OVRHand hand;
        readonly OVRSkeleton skeleton;
        readonly Transform eye;

        Transform thumbTip, indexTip, wrist, indexKnuckle, littleKnuckle, middleKnuckle, middleTip, ringKnuckle, ringTip;

        public HandReader(HandSide side, OVRHand hand, OVRSkeleton skeleton, Transform eye)
        {
            this.side = side;
            this.hand = hand;
            this.skeleton = skeleton;
            this.eye = eye;
        }

        public HandFrame Read()
        {
            var frame = new HandFrame { Side = side, Time = Time.time, EyePosition = eye.position, EyeRotation = eye.rotation };
            if (!FindBones()) return frame;

            frame.Visible = hand.IsTracked;
            frame.Tracked = hand.IsTracked
                && hand.GetFingerConfidence(OVRHand.HandFinger.Thumb) == OVRHand.TrackingConfidence.High
                && hand.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High;
            frame.Pinching = frame.Tracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            frame.ThumbTip = thumbTip.position;
            frame.IndexTip = indexTip.position;
            frame.Wrist = wrist.position;
            frame.IndexKnuckle = indexKnuckle.position;
            frame.LittleKnuckle = littleKnuckle.position;
            frame.MiddleKnuckle = middleKnuckle.position;
            frame.MiddleTip = middleTip.position;
            frame.RingKnuckle = ringKnuckle.position;
            frame.RingTip = ringTip.position;
            return frame;
        }

        bool FindBones()
        {
            if (thumbTip != null) return true;
            if (!skeleton.IsInitialized) return false;
            foreach (var bone in skeleton.Bones)
            {
                switch (bone.Id)
                {
                    case OVRSkeleton.BoneId.XRHand_ThumbTip: thumbTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_IndexTip: indexTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_Wrist: wrist = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_IndexProximal: indexKnuckle = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_LittleProximal: littleKnuckle = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_MiddleProximal: middleKnuckle = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_MiddleTip: middleTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_RingProximal: ringKnuckle = bone.Transform; break;
                    case OVRSkeleton.BoneId.XRHand_RingTip: ringTip = bone.Transform; break;
                }
            }
            if (thumbTip != null && indexTip != null && wrist != null && indexKnuckle != null && littleKnuckle != null
                && middleKnuckle != null && middleTip != null && ringKnuckle != null && ringTip != null) return true;
            thumbTip = null;
            return false;
        }
    }
}
