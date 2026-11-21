using Edith.Pairing;
using UnityEngine;

namespace Edith.Occlusion
{
    /// Draws the hand occluder mesh whenever the hand is tracked, at any confidence. OVRMeshRenderer's own toggle
    /// hides it while whole-hand confidence is low, as in a fist or a palm turned toward the face, and a snapshot
    /// behind the hand then draws over it.
    [RequireComponent(typeof(OVRHand), typeof(SkinnedMeshRenderer))]
    public sealed class TrackedHandOccluder : MonoBehaviour
    {
        OVRHand hand;
        SkinnedMeshRenderer mesh;

        void Awake()
        {
            hand = GetComponent<OVRHand>();
            mesh = GetComponent<SkinnedMeshRenderer>();
        }

        // Before pairing no snapshot hangs for the hand to hide, and nothing reads the hands.
        void LateUpdate()
        {
            var show = PairingGate.Open && hand.IsDataValid && hand.IsTracked;
            if (mesh.enabled != show) mesh.enabled = show;
        }
    }
}
