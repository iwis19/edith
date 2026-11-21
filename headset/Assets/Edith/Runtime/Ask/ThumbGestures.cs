using System;
using Edith.Pairing;
using UnityEngine;

namespace Edith.Ask
{
    public enum ThumbGesture { Tap, SwipeLeft, SwipeRight }

    /// The right hand's thumb microgestures that Ask uses: a tap on the side of the index finger and swipes left or
    /// right along it. `source` must read the right OVRHand.
    public sealed class ThumbGestures : MonoBehaviour
    {
        [SerializeField] OVRMicrogestureEventSource source;

        public event Action<ThumbGesture> Recognized;

        void OnEnable() => source.WhenGestureRecognized += OnGesture;
        void OnDisable() => source.WhenGestureRecognized -= OnGesture;

        void OnGesture(OVRHand.MicrogestureType type)
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
            Debug.Log($"[EDITH gesture] {gesture}");
            Recognized?.Invoke(gesture);
        }
    }
}
