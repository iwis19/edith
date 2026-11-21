using System;
using Edith.Hands;
using Edith.Pairing;
using UnityEngine;

namespace Edith.Ask
{
    public enum ThumbGesture { Tap, SwipeLeft, SwipeRight }

    /// The thumb microgestures of either hand that Ask uses: a tap on the side of the index finger and swipes left or
    /// right along it. `right` must read the right OVRHand and `left` the left one.
    public sealed class ThumbGestures : MonoBehaviour
    {
        [SerializeField] OVRMicrogestureEventSource right;
        [SerializeField] OVRMicrogestureEventSource left;

        public event Action<ThumbGesture> Recognized;

        void OnEnable()
        {
            right.WhenGestureRecognized += OnRight;
            left.WhenGestureRecognized += OnLeft;
        }

        void OnDisable()
        {
            right.WhenGestureRecognized -= OnRight;
            left.WhenGestureRecognized -= OnLeft;
        }

        void OnRight(OVRHand.MicrogestureType type) => OnGesture(HandSide.Right, type);
        void OnLeft(OVRHand.MicrogestureType type) => OnGesture(HandSide.Left, type);

        void OnGesture(HandSide side, OVRHand.MicrogestureType type)
        {
            if (!PairingGate.Open) return;
            ThumbGesture gesture;
            switch (type)
            {
                case OVRHand.MicrogestureType.ThumbTap: gesture = ThumbGesture.Tap; break;
                case OVRHand.MicrogestureType.SwipeLeft: gesture = ThumbGesture.SwipeLeft; break;
                case OVRHand.MicrogestureType.SwipeRight: gesture = ThumbGesture.SwipeRight; break;
                default: return;
            }
            Debug.Log($"[EDITH gesture] {gesture}, {side} hand");
            Recognized?.Invoke(gesture);
        }
    }
}
