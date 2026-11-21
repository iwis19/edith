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
        [SerializeField] RightHandSource hand;
        [SerializeField] GrabTuning tuning = new GrabTuning();

        readonly List<IGrabTargets> owners = new List<IGrabTargets>();
        readonly List<(int id, SightBox box)> targets = new List<(int id, SightBox box)>();
        GrabTracker tracker;
        GrabPhase loggedPhase;
        bool loggedClosed;

        /// Adds `owner`'s cards to the ones a fist can grab, from the next frame on.
        public void Register(IGrabTargets owner)
        {
            if (!owners.Contains(owner)) owners.Add(owner);
        }

        public void Unregister(IGrabTargets owner) => owners.Remove(owner);

        /// True from the grab until the hand opens; pinch ignores the hand meanwhile.
        public bool Claimed { get; private set; }

        /// Every frame's reading, so listeners see each event exactly once.
        public event Action<GrabReading> Stepped;

        void Awake() => tracker = new GrabTracker(tuning);

        void LateUpdate()
        {
            targets.Clear();
            for (var i = 0; i < owners.Count; i++)
                owners[i].AddGrabTargets(targets);
            var reading = tracker.Step(hand.Read(), targets);
            Claimed = reading.Claimed;
            LogCurl();
            Log(reading);
            Stepped?.Invoke(reading);
        }

        // Every close and open logs its curl, and an open the deepest curl of that fist, so GrabTuning can be set from real hands.
        void LogCurl()
        {
            if (tracker.IsClosed == loggedClosed) return;
            loggedClosed = tracker.IsClosed;
            Debug.Log(loggedClosed
                ? $"[EDITH grab] closed curl {tracker.LastCurl:F2}"
                : $"[EDITH grab] opened curl {tracker.LastCurl:F2}, deepest {tracker.DeepestCurl:F2}");
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
