using System;
using System.Collections.Generic;

namespace Edith.Scrapbook
{
    /// Keeps the `capacity` values used most recently, by key. A value pushed out goes to `evicted`.
    public sealed class RecentCache<T>
    {
        readonly int capacity;
        readonly Action<T> evicted;
        readonly Dictionary<int, T> values = new Dictionary<int, T>();
        // Least recently used first.
        readonly List<int> order = new List<int>();

        public RecentCache(int capacity, Action<T> evicted)
        {
            this.capacity = capacity;
            this.evicted = evicted;
        }

        public int Count => values.Count;

        /// Counts as a use of `key`.
        public bool TryGet(int key, out T value)
        {
            if (!values.TryGetValue(key, out value)) return false;
            order.Remove(key);
            order.Add(key);
            return true;
        }

        /// A value already held under `key` is evicted.
        public void Put(int key, T value)
        {
            if (values.TryGetValue(key, out var old))
            {
                order.Remove(key);
                evicted(old);
            }
            values[key] = value;
            order.Add(key);
            while (order.Count > capacity)
            {
                var oldest = order[0];
                order.RemoveAt(0);
                evicted(values[oldest]);
                values.Remove(oldest);
            }
        }

        /// Evicts everything.
        public void Clear()
        {
            foreach (var value in values.Values) evicted(value);
            values.Clear();
            order.Clear();
        }
    }
}
