using Edith.Pairing;
using UnityEngine;

namespace Edith.Hands
{
    /// Reads the tracked left hand and the eye into a HandFrame. HandFrame.PalmNormal assumes a right hand, so on
    /// these frames it points into the palm.
    public sealed class LeftHandSource : MonoBehaviour
    {
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] Transform eye;

        HandReader reader;

        public HandFrame Read() => (reader ??= new HandReader(hand, skeleton, eye)).Read().Gated(PairingGate.Open);
    }
}
