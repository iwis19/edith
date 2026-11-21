using System;
using Edith.Grab;
using Edith.Hands;
using UnityEngine;

namespace Edith.Pinch
{
    /// Steps the pinch tracker on the right hand once per frame, after hand tracking has updated.
    public sealed class PinchInput : MonoBehaviour
    {
        [SerializeField] RightHandSource hand;
        [SerializeField] PinchTuning tuning = new PinchTuning();
        /// While a fist holds or has just pocketed a card the hand is not pinching.
        [SerializeField] GrabInput grab;

        PinchTracker tracker;
        PinchPhase loggedPhase;
        PinchRejection loggedRejection;

        /// Every frame's reading, so listeners see each event exactly once.
        public event Action<PinchReading> Stepped;

        void Awake() => tracker = new PinchTracker(tuning);

        void LateUpdate()
        {
            var frame = hand.Read();
            if (grab != null && grab.Claimed) frame.Pinching = false;
            var reading = tracker.Step(frame);
            Log(reading);
            Stepped?.Invoke(reading);
        }

        void Log(in PinchReading reading)
        {
            if (reading.Event != PinchEvent.None)
                Debug.Log($"[EDITH pinch] {reading.Event}{(reading.Rejection != PinchRejection.None ? $" {reading.Rejection}" : "")}");
            if (reading.Phase != loggedPhase)
            {
                loggedPhase = reading.Phase;
                Debug.Log($"[EDITH pinch] phase {loggedPhase}");
            }
            if (reading.Event == PinchEvent.None && reading.Rejection != loggedRejection)
            {
                loggedRejection = reading.Rejection;
                if (loggedRejection != PinchRejection.None) Debug.Log($"[EDITH pinch] reject {loggedRejection}");
            }
        }
    }
}
