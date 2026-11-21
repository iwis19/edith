using UnityEngine;

namespace Edith.Hands
{
    /// Reads the tracked right hand and the eye into a HandFrame.
    public sealed class RightHandSource : MonoBehaviour
    {
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] Transform eye;

        Transform thumbTip, indexTip, wrist, indexKnuckle, littleKnuckle;

        public HandFrame Read()
        {
            var frame = new HandFrame { Time = Time.time, EyePosition = eye.position, EyeRotation = eye.rotation };
            if (!FindBones()) return frame;

            frame.Tracked = hand.IsTracked
                && hand.GetFingerConfidence(OVRHand.HandFinger.Thumb) == OVRHand.TrackingConfidence.High
                && hand.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High;
            frame.Pinching = frame.Tracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            frame.ThumbTip = thumbTip.position;
            frame.IndexTip = indexTip.position;
            frame.Wrist = wrist.position;
            frame.IndexKnuckle = indexKnuckle.position;
            frame.LittleKnuckle = littleKnuckle.position;
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
                }
            }
            if (thumbTip != null && indexTip != null && wrist != null && indexKnuckle != null && littleKnuckle != null) return true;
            thumbTip = null;
            return false;
        }
    }
}
