using UnityEngine;

namespace Edith.Hands
{
    /// Reads the tracked right hand and the eye into a HandFrame.
    public sealed class RightHandSource : MonoBehaviour
    {
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] Transform eye;

        HandReader reader;

        public HandFrame Read() => (reader ??= new HandReader(hand, skeleton, eye)).Read();
    }
}
