using System;
using System.Collections.Generic;
using Edith.Hands;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Grab
{
    /// Steps the grab tracker on the right hand once per frame, before PinchInput so a fist is claimed before pinch reads it.
    [DefaultExecutionOrder(-50)]
    public sealed class GrabInput : MonoBehaviour
    {
        static readonly IReadOnlyList<(int id, SightBox box)> NoCards = Array.Empty<(int id, SightBox box)>();

        [SerializeField] RightHandSource hand;
        [SerializeField] GrabTuning tuning = new GrabTuning();

        GrabTracker tracker;
        GrabPhase loggedPhase;

        /// The cards a fist can grab, set by whoever owns the snapshots.
        public Func<IReadOnlyList<(int id, SightBox box)>> Cards { get; set; }

        /// True from the grab until the hand opens; pinch ignores the hand meanwhile.
        public bool Claimed { get; private set; }

        /// Every frame's reading, so listeners see each event exactly once.
        public event Action<GrabReading> Stepped;

        void Awake() => tracker = new GrabTracker(tuning);

        void LateUpdate()
        {
            var reading = tracker.Step(hand.Read(), Cards?.Invoke() ?? NoCards);
            Claimed = reading.Claimed;
            Log(reading);
            Stepped?.Invoke(reading);
        }

        void Log(in GrabReading reading)
        {
            if (reading.Event != GrabEvent.None)
                Debug.Log($"[EDITH grab] {reading.Event} card {reading.CardId}");
            if (reading.Phase != loggedPhase)
            {
                loggedPhase = reading.Phase;
                Debug.Log($"[EDITH grab] phase {loggedPhase}");
            }
        }
    }
}
