using System;
using System.Collections.Generic;
using System.Linq;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// One rarity chip under the deck.
    public readonly struct Chip
    {
        public readonly string Label;
        public readonly Rarity Tier;
        public readonly bool Selected;
        /// False for a tier with no memory: it is drawn dimmed and a pinch passes through it.
        public readonly bool Enabled;

        public Chip(Rarity tier, int count, bool selected)
        {
            Label = $"{tier} {count}";
            Tier = tier;
            Selected = selected;
            Enabled = count > 0;
        }
    }

    /// What the Scrapbook is showing of the collected memories: every memory, or only those of one rarity, ordered
    /// from Common to Mythic with the memories that have no tier first, newest first within a tier.
    public sealed class Browse
    {
        static readonly Rarity[] Tiers = (Rarity[])Enum.GetValues(typeof(Rarity));

        readonly IReadOnlyList<Memory> all;
        readonly int[] counts = new int[Tiers.Length];
        readonly List<Memory> visible = new List<Memory>();

        /// `newestFirst` is the backend's order.
        public Browse(IReadOnlyList<Memory> newestFirst)
        {
            // OrderBy is stable, so the newest-first order holds within a tier.
            all = newestFirst.OrderBy(memory => memory.Tier is Rarity tier ? (int)tier : -1).ToList();
            foreach (var memory in all)
                if (memory.Tier is Rarity tier) counts[(int)tier]++;
            Refresh();
        }

        /// Null shows every memory.
        public Rarity? Filter { get; private set; }

        /// The memories the deck shows.
        public IReadOnlyList<Memory> Visible => visible;

        /// How many memories of `tier` there are, whatever the filter.
        public int Count(Rarity tier) => counts[(int)tier];

        /// Narrows to `tier`. Choosing the tier already in use, or null, shows every memory again.
        public void ToggleFilter(Rarity? tier)
        {
            Filter = tier == Filter ? null : tier;
            Refresh();
        }

        /// The index in Visible of the memory with `id`, or -1.
        public int IndexOf(int id)
        {
            for (var i = 0; i < visible.Count; i++)
                if (visible[i].Id == id) return i;
            return -1;
        }

        /// One chip per tier, rarest last, whether or not it has a memory.
        public void Chips(List<Chip> into)
        {
            into.Clear();
            foreach (var tier in Tiers) into.Add(new Chip(tier, Count(tier), Filter == tier));
        }

        void Refresh()
        {
            visible.Clear();
            foreach (var memory in all)
                if (Filter == null || memory.Tier == Filter) visible.Add(memory);
        }
    }
}
