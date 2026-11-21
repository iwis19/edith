using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edith.Snapshots
{
    /// The snapshots in the room. Look calls Preview while a box is dragged and Place on release.
    public sealed class SnapshotRoom
    {
        readonly Transform parent;
        readonly SnapshotShelf<Snapshot> shelf = new SnapshotShelf<Snapshot>();
        List<Snapshot> covered = new List<Snapshot>();
        List<Snapshot> under = new List<Snapshot>();

        /// `parent` must have unit world scale.
        public SnapshotRoom(Transform parent) => this.parent = parent;

        /// Oldest first.
        public IReadOnlyList<(Snapshot item, SightBox box)> Snapshots => shelf.Items;

        /// Fires as a snapshot leaves the room into the pocket, while it still exists.
        public event Action<Snapshot> Pocketed;

        /// Fades out the snapshots `dragged` covers and brings back the ones it no longer covers.
        public void Preview(in SightBox dragged)
        {
            shelf.Under(dragged, under);
            foreach (var snapshot in covered)
                if (!under.Contains(snapshot)) snapshot.SetCovered(false);
            foreach (var snapshot in under)
                snapshot.SetCovered(true);
            (covered, under) = (under, covered);
        }

        /// Brings back every snapshot the dragged box was covering, for a drag that ends without a snapshot.
        public void CancelPreview()
        {
            foreach (var snapshot in covered)
                snapshot.SetCovered(false);
            covered.Clear();
        }

        /// Hangs a snapshot in `box`. The ones it covers and the oldest past four fade out and are destroyed.
        public Snapshot Place(in SightBox box, Texture photo = null)
        {
            var snapshot = Snapshot.Create(box, parent, photo);
            foreach (var gone in shelf.Add(snapshot, box))
                gone.Leave();
            CancelPreview();
            return snapshot;
        }

        /// Fades out and destroys `snapshot`. False when it is not in the room.
        public bool Dismiss(Snapshot snapshot)
        {
            if (!Take(snapshot)) return false;
            snapshot.Leave();
            return true;
        }

        /// Takes `snapshot` off the shelf and shrinks it into `point`, then destroys it. False when it is not in the room.
        public bool Pocket(Snapshot snapshot, Vector3 point)
        {
            if (!Take(snapshot)) return false;
            snapshot.Pocket(point);
            Pocketed?.Invoke(snapshot);
            return true;
        }

        bool Take(Snapshot snapshot)
        {
            if (!shelf.Remove(snapshot)) return false;
            covered.Remove(snapshot);
            return true;
        }
    }
}
