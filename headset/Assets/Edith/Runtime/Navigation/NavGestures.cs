using Edith.Ask;
using Edith.Hands;
using UnityEngine;

namespace Edith.Navigation
{
    /// The directions gestures. Both hands as finger guns pointing straight ahead open a question in which EDITH asks
    /// "Where's our next adventure?"; both pointing up stop the directions under way. The hand sources are gated, so
    /// neither works until the phone is paired.
    public sealed class NavGestures : MonoBehaviour
    {
        [SerializeField] RightHandSource right;
        [SerializeField] LeftHandSource left;
        [SerializeField] AskController ask;
        [SerializeField] NavArrow arrow;
        [SerializeField] FingerGunTuning tuning = new FingerGunTuning();

        FingerGunPair pair;

        void Awake() => pair = new FingerGunPair(tuning);

        // After hand tracking has updated, like the pinch.
        void LateUpdate()
        {
            if (right == null || left == null) return;
            switch (pair.Step(right.Read(), left.Read()))
            {
                case NavGesture.Start:
                    Debug.Log("[EDITH nav] finger guns forward: asking where to go");
                    if (ask != null) ask.BeginNavigation();
                    return;
                case NavGesture.Stop:
                    Debug.Log("[EDITH nav] finger guns up: stopping directions");
                    if (arrow != null) arrow.Cancel();
                    return;
            }
        }
    }
}
