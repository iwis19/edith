using System.Collections.Generic;
using Edith.Look;
using Edith.Snapshots;
using UnityEngine;

namespace Edith.Grab
{
    /// Applies grab readings to the snapshots of a room: a fist carries one, opening floats it back, and pulling it to
    /// the chest pockets it.
    public sealed class SnapshotGrab : MonoBehaviour, IGrabTargets
    {
        [SerializeField] GrabInput input;

        SnapshotRoom room;
        Snapshot held;
        // Destroyed once it has shrunk away, which Unity's null check sees.
        Snapshot pocketing;
        Vector3 gripAtGrab;
        Quaternion handAtGrab;
        Vector3 cardPositionAtGrab;
        Quaternion cardRotationAtGrab;

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

        void OnStepped(GrabReading reading)
        {
            switch (reading.Event)
            {
                case GrabEvent.Grabbed:
                    Pick(reading);
                    break;
                case GrabEvent.Dropped:
                    if (held != null) held.FloatBack();
                    held = null;
                    return;
                case GrabEvent.Pocketed:
                    if (held != null && room.Pocket(held, reading.Grip)) pocketing = held;
                    held = null;
                    return;
            }
            if (held != null && reading.Phase == GrabPhase.Holding && reading.HasGrip) Carry(reading);
            // Reads as going into the hand, rather than into the spot where the pull ended.
            else if (pocketing != null && reading.HasGrip) pocketing.PocketToward(reading.Grip);
        }

        void Pick(in GrabReading reading)
        {
            held = null;
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
                if (snapshots[i].item.Id == reading.CardId) held = snapshots[i].item;
            if (held == null) return;
            if (!CanCarry(held))
            {
                Debug.Log($"[EDITH grab] snapshot {held.Id} is {held.Card.Phase}, so it stays put");
                held = null;
                return;
            }

            gripAtGrab = reading.Grip;
            handAtGrab = reading.Rotation;
            cardPositionAtGrab = held.transform.position;
            cardRotationAtGrab = held.transform.rotation;
        }

        /// Keeps the card where the fist took it, turning with the wrist about the grip.
        void Carry(in GrabReading reading)
        {
            var turn = reading.Rotation * Quaternion.Inverse(handAtGrab);
            held.Hold(reading.Grip + turn * (cardPositionAtGrab - gripAtGrab), turn * cardRotationAtGrab);
        }
    }
}
