using System;
using System.Collections.Generic;
using Edith.Hands;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Grab
{
    /// Steps the grab of both hands once per frame, before PinchInput so a fist is claimed before pinch reads it.
    [DefaultExecutionOrder(-50)]
    public sealed class GrabInput : MonoBehaviour
    {
        [SerializeField] RightHandSource right;
        [SerializeField] LeftHandSource left;
        [SerializeField] GrabTuning tuning = new GrabTuning();

        readonly List<IGrabTargets> owners = new List<IGrabTargets>();
        readonly List<(int id, SightBox box)> targets = new List<(int id, SightBox box)>();
        readonly bool[] claimed = new bool[2];
        readonly GrabPhase[] loggedPhase = new GrabPhase[2];
        readonly bool[] loggedClosed = new bool[2];
        GrabPair pair;

        /// Adds `owner`'s cards to the ones a fist can grab, from the next frame on.
        public void Register(IGrabTargets owner)
        {
            if (!owners.Contains(owner)) owners.Add(owner);
        }

        public void Unregister(IGrabTargets owner) => owners.Remove(owner);

        /// True from that hand's grab until it opens; pinch ignores the hand meanwhile.
        public bool Claimed(HandSide side) => claimed[(int)side];

        /// Every frame's reading of each hand, the right hand first, so listeners see each event exactly once.
        public event Action<HandSide, GrabReading> Stepped;

        void Awake() => pair = new GrabPair(tuning);

        void LateUpdate()
        {
            targets.Clear();
            for (var i = 0; i < owners.Count; i++)
                owners[i].AddGrabTargets(targets);
            var (rightReading, leftReading) = pair.Step(right.Read(), left.Read(), targets);
            Report(HandSide.Right, rightReading);
            Report(HandSide.Left, leftReading);
        }

        void Report(HandSide side, in GrabReading reading)
        {
            claimed[(int)side] = reading.Claimed;
            LogCurl(side);
            Log(side, reading);
            Stepped?.Invoke(side, reading);
        }

        // Every close and open logs its curl, and an open the deepest curl of that fist, so GrabTuning can be set from real hands.
        void LogCurl(HandSide side)
        {
            var tracker = pair.Tracker(side);
            if (tracker.IsClosed == loggedClosed[(int)side]) return;
            loggedClosed[(int)side] = tracker.IsClosed;
            Debug.Log(tracker.IsClosed
                ? $"[EDITH grab] {side} closed curl {tracker.LastCurl:F2}"
                : $"[EDITH grab] {side} opened curl {tracker.LastCurl:F2}, deepest {tracker.DeepestCurl:F2}");
        }

        void Log(HandSide side, in GrabReading reading)
        {
            if (reading.Event != GrabEvent.None)
                Debug.Log($"[EDITH grab] {side} {reading.Event} card {reading.CardId}");
            if (reading.Phase != loggedPhase[(int)side])
            {
                loggedPhase[(int)side] = reading.Phase;
                Debug.Log($"[EDITH grab] {side} phase {reading.Phase}");
            }
        }
    }
}
