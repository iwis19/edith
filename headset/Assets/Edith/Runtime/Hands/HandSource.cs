using Edith.Pairing;
using UnityEngine;

namespace Edith.Hands
{
    /// Reads one tracked hand and the eye into a HandFrame, unseen while the pairing gate is closed.
    public abstract class HandSource : MonoBehaviour
    {
        [SerializeField] OVRHand hand;
        [SerializeField] OVRSkeleton skeleton;
        [SerializeField] Transform eye;

        HandReader reader;

        public abstract HandSide Side { get; }

        public HandFrame Read() => (reader ??= new HandReader(Side, hand, skeleton, eye)).Read().Gated(PairingGate.Open);
    }
}
