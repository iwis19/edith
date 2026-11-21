using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edith.Snapshots
{
    /// The snapshots in the room. Look calls Preview while a box is dragged and Place on release.
    public sealed class SnapshotRoom
    {
        readonly Transform parent;
        readonly Material material;
        readonly Font font;
        readonly SnapshotShelf<Snapshot> shelf = new SnapshotShelf<Snapshot>();
        List<Snapshot> covered = new List<Snapshot>();
        List<Snapshot> under = new List<Snapshot>();

        /// `parent` must have unit world scale. `material`, when given, draws every photo and card so real objects in
        /// front of them hide them. `font` writes the cards' text.
        public SnapshotRoom(Transform parent, Material material = null, Font font = null)
        {
            this.parent = parent;
            this.material = material;
            this.font = font;
        }

        /// Oldest first.
        public IReadOnlyList<(Snapshot item, SightBox box)> Snapshots => shelf.Items;

        /// True from the first Preview of a drag until it ends in Place or CancelPreview.
        public bool Drawing { get; private set; }

        /// Fires as a snapshot leaves the room into the pocket, while it still exists.
        public event Action<Snapshot> Pocketed;

        /// Fades out the snapshots `dragged` covers and brings back the ones it no longer covers.
        public void Preview(in SightBox dragged)
        {
            Drawing = true;
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
            Drawing = false;
            foreach (var snapshot in covered)
                snapshot.SetCovered(false);
            covered.Clear();
        }

        /// Hangs a snapshot in `box`. The ones it covers and the oldest past four fade out and are destroyed.
        public Snapshot Place(in SightBox box, Texture photo = null)
        {
            var snapshot = Snapshot.Create(box, parent, photo, material, font);
            foreach (var gone in shelf.Add(snapshot, box))
                gone.Leave();
            CancelPreview();
            return snapshot;
        }

        /// The newest snapshot whose box lies on the line from `eye` through `point`, or null. A point off the box
        /// hits nothing, however near.
        public Snapshot Hit(Vector3 eye, Vector3 point)
        {
            var items = shelf.Items;
            for (var i = items.Count - 1; i >= 0; i--)
                if (!items[i].item.Leaving && items[i].box.InLine(eye, point, 0f)) return items[i].item;
            return null;
        }

        /// Fades out and destroys `snapshot`. False when it is not in the room.
        public bool Dismiss(Snapshot snapshot)
        {
            if (!Take(snapshot)) return false;
            snapshot.Leave();
            return true;
        }

        /// Fades out and destroys every snapshot, as a tap on each would. Returns how many there were.
        public int Clear()
        {
            var all = new List<Snapshot>(shelf.Count);
            foreach (var (item, _) in shelf.Items) all.Add(item);
            foreach (var snapshot in all) Dismiss(snapshot);
            return all.Count;
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
