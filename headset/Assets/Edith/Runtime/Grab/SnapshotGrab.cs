using System.Collections.Generic;
using Edith.Hands;
using Edith.Look;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Grab
{
    /// Applies grab readings to the snapshots of a room: a fist of either hand carries one, opening floats it back,
    /// and pulling it to the chest pockets it. Each hand carries its own snapshot.
    public sealed class SnapshotGrab : MonoBehaviour, IGrabTargets
    {
        [SerializeField] GrabInput input;

        sealed class Carried
        {
            public Snapshot Held;
            // Destroyed once it has shrunk away, which Unity's null check sees.
            public Snapshot Pocketing;
            public Vector3 GripAtGrab;
            public Quaternion HandAtGrab;
            public Vector3 CardPositionAtGrab;
            public Quaternion CardRotationAtGrab;
        }

        readonly Carried[] hands = { new Carried(), new Carried() };
        SnapshotRoom room;

        /// The room whose snapshots a fist can grab. Call before the first frame with a hand.
        public void Bind(SnapshotRoom room) => this.room = room;

        void OnEnable()
        {
            input.Register(this);
            input.Stepped += OnStepped;
        }

        void OnDisable()
        {
            input.Stepped -= OnStepped;
            input.Unregister(this);
        }

        /// Every snapshot is a target, so a fist closed on one is claimed and cannot read as a pinch, even when the
        /// snapshot cannot move yet.
        public void AddGrabTargets(List<(int id, SightBox box)> targets)
        {
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
                targets.Add((snapshots[i].item.Id, snapshots[i].box));
        }

        /// Only a Look that finished well moves: one still being written stays put so its fact is read first, and
        /// one that failed never moves.
        public static bool CanCarry(Snapshot snapshot) => snapshot.Card.Phase == LookPhase.Done;

        void OnStepped(HandSide side, GrabReading reading)
        {
            var hand = hands[(int)side];
            switch (reading.Event)
            {
                case GrabEvent.Grabbed:
                    Pick(hand, reading);
                    break;
                case GrabEvent.Dropped:
                    if (hand.Held != null) hand.Held.FloatBack();
                    hand.Held = null;
                    return;
                case GrabEvent.Pocketed:
                    if (hand.Held != null && room.Pocket(hand.Held, reading.Grip))
                        hand.Pocketing = hand.Held;
                    hand.Held = null;
                    return;
            }
            if (hand.Held != null && reading.Phase == GrabPhase.Holding && reading.HasGrip) Carry(hand, reading);
            // Reads as going into the hand, rather than into the spot where the pull ended.
            else if (hand.Pocketing != null && reading.HasGrip) hand.Pocketing.PocketToward(reading.Grip);
        }

        void Pick(Carried hand, in GrabReading reading)
        {
            hand.Held = null;
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
                if (snapshots[i].item.Id == reading.CardId) hand.Held = snapshots[i].item;
            if (hand.Held == null) return;
            if (!CanCarry(hand.Held))
            {
                Debug.Log($"[EDITH grab] snapshot {hand.Held.Id} is {hand.Held.Card.Phase}, so it stays put");
                hand.Held = null;
                return;
            }

            hand.GripAtGrab = reading.Grip;
            hand.HandAtGrab = reading.Rotation;
            hand.CardPositionAtGrab = hand.Held.transform.position;
            hand.CardRotationAtGrab = hand.Held.transform.rotation;
        }

        /// Keeps the card where the fist took it, turning with the wrist about the grip.
        static void Carry(Carried hand, in GrabReading reading)
        {
            var turn = reading.Rotation * Quaternion.Inverse(hand.HandAtGrab);
            hand.Held.Hold(reading.Grip + turn * (hand.CardPositionAtGrab - hand.GripAtGrab), turn * hand.CardRotationAtGrab);
        }
    }
}
