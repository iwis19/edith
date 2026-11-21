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

        /// The hand as tracked, even while the gate is closed. Only the welcome reads it, to tap its own buttons
        /// before a phone is paired; every gesture reads Read.
        public HandFrame ReadUngated() => (reader ??= new HandReader(Side, hand, skeleton, eye)).Read();

        public HandFrame Read() => (reader ??= new HandReader(Side, hand, skeleton, eye)).Read().Gated(PairingGate.Open);
    }
}
