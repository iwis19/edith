using System;
using Edith.Grab;
using Edith.Hands;
using UnityEngine;

namespace Edith.Pinch
{
    /// Steps the pinch of both hands once per frame, after hand tracking has updated. PinchPair lets one hand pinch
    /// at a time, so listeners hear a single pinch.
    public sealed class PinchInput : MonoBehaviour
    {
        [SerializeField] RightHandSource right;
        [SerializeField] LeftHandSource left;
        [SerializeField] PinchTuning tuning = new PinchTuning();
        /// While a fist holds or has just pocketed a card that hand is not pinching.
        [SerializeField] GrabInput grab;

        PinchPair pair;
        PinchPhase loggedPhase;
        PinchRejection loggedRejection;

        /// Every frame's reading, so listeners see each event exactly once.
        public event Action<PinchReading> Stepped;

        /// The hand whose pinch the readings follow, or null while neither hand pinches.
        public HandSide? Owner => pair?.Owner;

        void Awake() => pair = new PinchPair(tuning);

        void LateUpdate()
        {
            var reading = pair.Step(Read(right), Read(left));
            Log(reading);
            Stepped?.Invoke(reading);
        }

        HandFrame Read(HandSource hand)
        {
            var frame = hand.Read();
            if (grab != null && grab.Claimed(hand.Side)) frame.Pinching = false;
            return frame;
        }

        void Log(in PinchReading reading)
        {
            if (reading.Event != PinchEvent.None)
                Debug.Log($"[EDITH pinch] {reading.Event}{(reading.Rejection != PinchRejection.None ? $" {reading.Rejection}" : "")}");
            if (reading.Phase != loggedPhase)
            {
                loggedPhase = reading.Phase;
                Debug.Log($"[EDITH pinch] phase {loggedPhase}{(pair.Owner is HandSide side ? $", {side} hand" : "")}");
            }
            if (reading.Event == PinchEvent.None && reading.Rejection != loggedRejection)
            {
                loggedRejection = reading.Rejection;
                if (loggedRejection != PinchRejection.None) Debug.Log($"[EDITH pinch] reject {loggedRejection}");
            }
        }
    }
}
