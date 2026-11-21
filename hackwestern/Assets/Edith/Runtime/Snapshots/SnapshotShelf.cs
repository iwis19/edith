using System.Collections.Generic;
using UnityEngine;

namespace Edith.Snapshots
{
    /// Which snapshots stay in the room: at most `capacity`, oldest first. A new box erases every snapshot it covers
    /// as the user sees it, then the oldest go until the shelf is back under capacity.
    public sealed class SnapshotShelf<T> where T : class
    {
        readonly int capacity;
        readonly float minOverlap;
        readonly List<(T item, SightBox box)> items = new List<(T, SightBox)>();

        /// `minOverlap` is a fraction of the smaller of the two boxes, so grazing edges do not count as covering.
        public SnapshotShelf(int capacity = 4, float minOverlap = 0.15f)
        {
            this.capacity = capacity;
            this.minOverlap = minOverlap;
        }

        public int Count => items.Count;

        /// Oldest first.
        public IReadOnlyList<(T item, SightBox box)> Items => items;

        /// Adds `item` and returns what must leave the room: the snapshots under `box`, then the oldest past capacity.
        public List<T> Add(T item, SightBox box)
        {
            var removed = new List<T>();
            Under(box, removed);
            items.RemoveAll(entry => removed.Contains(entry.item));
            items.Add((item, box));
            while (items.Count > capacity)
            {
                removed.Add(items[0].item);
                items.RemoveAt(0);
            }
            return removed;
        }

        /// Fills `under` with the snapshots that `box` covers as seen from its eye. Clears `under` first.
        public void Under(in SightBox box, List<T> under)
        {
            under.Clear();
            foreach (var (item, photo) in items)
                if (Covers(box, photo)) under.Add(item);
        }

        public bool Remove(T item) => items.RemoveAll(entry => entry.item == item) > 0;

        bool Covers(in SightBox box, in SightBox photo)
        {
            if (photo.SeenFrom(box.Eye) is not { } seen) return false;
            var view = box.Tangent;
            var width = Mathf.Min(view.xMax, seen.xMax) - Mathf.Max(view.xMin, seen.xMin);
            var height = Mathf.Min(view.yMax, seen.yMax) - Mathf.Max(view.yMin, seen.yMin);
            if (width <= 0f || height <= 0f) return false;
            var smaller = Mathf.Min(view.width * view.height, seen.width * seen.height);
            return smaller > 0f && width * height / smaller >= minOverlap;
        }
    }
}
