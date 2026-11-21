using System;
using System.Collections.Generic;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// A way to reach the past. Each lens has a tab on the lens bar and its own row of chips under it.
    public enum Lens { Time, Rarity }

    public enum ChipKind { Tab, Date, AllTiers, Tier }

    /// One thing on the lens bar a pinch can choose. Only the field of its Kind is set.
    public readonly struct Chip
    {
        public readonly ChipKind Kind;
        public readonly string Label;
        public readonly bool Selected;
        public readonly Lens Lens;
        public readonly DateTime Date;
        public readonly Rarity Tier;

        Chip(ChipKind kind, string label, bool selected, Lens lens = default, DateTime date = default, Rarity tier = default)
        {
            Kind = kind;
            Label = label;
            Selected = selected;
            Lens = lens;
            Date = date;
            Tier = tier;
        }

        public static Chip Tab(Lens lens, bool selected) => new Chip(ChipKind.Tab, lens.ToString(), selected, lens: lens);
        public static Chip Day(DateTime date, bool selected) => new Chip(ChipKind.Date, Memory.Day(date), selected, date: date);
        public static Chip All(int count, bool selected) => new Chip(ChipKind.AllTiers, $"All {count}", selected);
        public static Chip Of(Rarity tier, int count, bool selected) => new Chip(ChipKind.Tier, $"{tier} {count}", selected, tier: tier);
    }

    /// What the Scrapbook is showing of the collected memories: every memory newest first, the lens in use, and the
    /// rarity the Rarity lens narrows them to. The tier filter belongs to the Rarity lens, so leaving that lens
    /// clears it.
    public sealed class Browse
    {
        /// The most date chips shown at once.
        public const int DateChips = 5;

        static readonly Rarity[] Tiers = (Rarity[])Enum.GetValues(typeof(Rarity));

        readonly IReadOnlyList<Memory> all;
        readonly int[] counts = new int[Tiers.Length];
        readonly List<Memory> visible = new List<Memory>();
        readonly List<DateTime> dates = new List<DateTime>();

        /// `newestFirst` is kept in its order.
        public Browse(IReadOnlyList<Memory> newestFirst)
        {
            all = newestFirst;
            foreach (var memory in all)
                if (memory.Tier is Rarity tier) counts[(int)tier]++;
            Refresh();
        }

        public Lens Lens { get; private set; }

        /// Null shows every memory.
        public Rarity? Filter { get; private set; }

        /// The memories the deck shows, newest first.
        public IReadOnlyList<Memory> Visible => visible;

        /// The days that have a visible memory, newest first.
        public IReadOnlyList<DateTime> Dates => dates;

        /// How many memories of `tier` there are, whatever the filter.
        public int Count(Rarity tier) => counts[(int)tier];

        public void SetLens(Lens lens)
        {
            if (lens == Lens) return;
            Lens = lens;
            if (lens != Lens.Rarity) SetFilter(null);
        }

        /// Narrows to `tier`. Choosing the tier already in use, or null, shows every memory again.
        public void ToggleFilter(Rarity? tier) => SetFilter(tier == Filter ? null : tier);

        /// The first visible memory of that day, or -1.
        public int IndexOfDate(DateTime date)
        {
            for (var i = 0; i < visible.Count; i++)
                if (visible[i].Date == date.Date) return i;
            return -1;
        }

        /// The index in Visible of the memory with `id`, or -1.
        public int IndexOf(int id)
        {
            for (var i = 0; i < visible.Count; i++)
                if (visible[i].Id == id) return i;
            return -1;
        }

        /// One tab per lens, in the order of the enum.
        public void Tabs(List<Chip> into)
        {
            into.Clear();
            foreach (Lens lens in Enum.GetValues(typeof(Lens))) into.Add(Chip.Tab(lens, lens == Lens));
        }

        /// The chips under the tabs. Time: up to DateChips days, newest first, around `centred`, the day of the card
        /// in the middle of the deck. Rarity: "All" and one chip per tier that has a memory.
        public void Chips(DateTime? centred, List<Chip> into)
        {
            into.Clear();
            switch (Lens)
            {
                case Lens.Time:
                    var at = centred is DateTime day ? dates.IndexOf(day.Date) : -1;
                    var first = Math.Max(0, Math.Min(Math.Max(at, 0) - DateChips / 2, dates.Count - DateChips));
                    for (var i = first; i < dates.Count && i < first + DateChips; i++) into.Add(Chip.Day(dates[i], i == at));
                    break;
                case Lens.Rarity:
                    into.Add(Chip.All(all.Count, Filter == null));
                    foreach (var tier in Tiers)
                        if (Count(tier) > 0) into.Add(Chip.Of(tier, Count(tier), Filter == tier));
                    break;
            }
        }

        void SetFilter(Rarity? tier)
        {
            if (tier == Filter) return;
            Filter = tier;
            Refresh();
        }

        void Refresh()
        {
            visible.Clear();
            dates.Clear();
            foreach (var memory in all)
            {
                if (Filter != null && memory.Tier != Filter) continue;
                visible.Add(memory);
                if (dates.Count == 0 || dates[dates.Count - 1] != memory.Date) dates.Add(memory.Date);
            }
        }
    }
}
