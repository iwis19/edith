using System.Collections.Generic;
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

        /// A snapshot is a target once its card has finished, so the fact is read before the card can be put away.
        public void AddGrabTargets(List<(int id, SightBox box)> targets)
        {
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
                if (snapshots[i].item.Card.Finished) targets.Add((snapshots[i].item.Id, snapshots[i].box));
        }

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
                    if (held != null) room.Pocket(held, reading.Grip);
                    held = null;
                    return;
            }
            if (held != null && reading.Phase == GrabPhase.Holding && reading.HasGrip) Carry(reading);
        }

        void Pick(in GrabReading reading)
        {
            held = null;
            if (room == null) return;
            var snapshots = room.Snapshots;
            for (var i = 0; i < snapshots.Count; i++)
                if (snapshots[i].item.Id == reading.CardId) held = snapshots[i].item;
            if (held == null) return;

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
