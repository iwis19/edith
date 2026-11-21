using System;
using System.Collections.Generic;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// A way to reach the past. Each lens has a tab on the lens bar, in this order, and its own row of chips under it.
    public enum Lens { Places, Time, Rarity }

    public enum ChipKind { Tab, Date, AllTiers, Tier, City, Globe, Spot }

    /// One thing on the lens bar a pinch can choose. Only the field of its Kind is set.
    public readonly struct Chip
    {
        // A longer place name is cut to this many characters, so a row of place chips fits the bar.
        const int LabelLength = 18;

        public readonly ChipKind Kind;
        public readonly string Label;
        public readonly bool Selected;
        public readonly Lens Lens;
        public readonly DateTime Date;
        public readonly Rarity Tier;
        public readonly City City;
        public readonly Spot Spot;

        Chip(ChipKind kind, string label, bool selected, Lens lens = default, DateTime date = default, Rarity tier = default,
            City city = null, Spot spot = null)
        {
            Kind = kind;
            Label = label;
            Selected = selected;
            Lens = lens;
            Date = date;
            Tier = tier;
            City = city;
            Spot = spot;
        }

        public static Chip Tab(Lens lens, bool selected) => new Chip(ChipKind.Tab, lens.ToString(), selected, lens: lens);
        public static Chip Day(DateTime date, bool selected) => new Chip(ChipKind.Date, Memory.Day(date), selected, date: date);
        public static Chip All(int count, bool selected) => new Chip(ChipKind.AllTiers, $"All {count}", selected);
        public static Chip Of(Rarity tier, int count, bool selected) => new Chip(ChipKind.Tier, $"{tier} {count}", selected, tier: tier);
        public static Chip Of(City city) => new Chip(ChipKind.City, Counted(city), false, city: city);
        public static Chip Of(Spot spot, bool selected) => new Chip(ChipKind.Spot, Counted(spot), selected, spot: spot);
        public static Chip Globe() => new Chip(ChipKind.Globe, "Globe", false);

        static string Counted(Place place) =>
            $"{(place.Label.Length > LabelLength ? place.Label.Substring(0, LabelLength - 1).TrimEnd() + "…" : place.Label)} {place.Count}";
    }

    /// What the Scrapbook is showing of the collected memories: every memory newest first, the lens in use, and what
    /// that lens narrows them to. The Rarity lens narrows to a tier. The Places lens is at the globe, showing every
    /// memory, or dived into a city, showing that city's memories or those of one spot in it. A narrowing belongs to
    /// its lens, so leaving the lens clears it.
    public sealed class Browse
    {
        /// The most date chips shown at once.
        public const int DateChips = 5;
        /// The most city chips, or spot chips, shown at once. The rest are reached by their pins.
        public const int PlaceChips = 4;

        static readonly Rarity[] Tiers = (Rarity[])Enum.GetValues(typeof(Rarity));

        readonly IReadOnlyList<Memory> all;
        readonly int[] counts = new int[Tiers.Length];
        readonly List<Memory> visible = new List<Memory>();
        readonly List<DateTime> dates = new List<DateTime>();

        /// `newestFirst` is kept in its order. The lens is Places when any memory has a point, else Time.
        public Browse(IReadOnlyList<Memory> newestFirst)
        {
            all = newestFirst;
            foreach (var memory in all)
                if (memory.Tier is Rarity tier) counts[(int)tier]++;
            Atlas = new Atlas(all);
            Lens = Atlas.Cities.Count > 0 ? Lens.Places : Lens.Time;
            Refresh();
        }

        public Atlas Atlas { get; }

        public Lens Lens { get; private set; }

        /// Null shows every memory.
        public Rarity? Filter { get; private set; }

        /// The city the Places lens has dived into, or null at the globe.
        public City City { get; private set; }

        /// The spot of City the deck is narrowed to, or null.
        public Spot Spot { get; private set; }

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
            Filter = null;
            City = null;
            Spot = null;
            Refresh();
        }

        /// Narrows to `tier`. Choosing the tier already in use, or null, shows every memory again.
        public void ToggleFilter(Rarity? tier)
        {
            Filter = tier == Filter ? null : tier;
            Refresh();
        }

        /// Dives the Places lens into `city`, showing its memories.
        public void Dive(City city)
        {
            SetLens(Lens.Places);
            City = city;
            Spot = null;
            Refresh();
        }

        /// Returns the Places lens to the globe, showing every memory.
        public void Surface()
        {
            City = null;
            Spot = null;
            Refresh();
        }

        /// Narrows the city dived into to `spot`. Choosing the spot already in use shows the whole city again.
        public void ToggleSpot(Spot spot)
        {
            if (City == null) return;
            Spot = spot == Spot ? null : spot;
            Refresh();
        }

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

        /// The chips under the tabs. Places: at the globe one chip per city, and in a city "Globe" and one chip per
        /// spot. Time: up to DateChips days, newest first, around `centred`, the day of the card in the middle of the
        /// deck. Rarity: "All" and one chip per tier that has a memory.
        public void Chips(DateTime? centred, List<Chip> into)
        {
            into.Clear();
            switch (Lens)
            {
                case Lens.Places when City == null:
                    for (var i = 0; i < Atlas.Cities.Count && i < PlaceChips; i++) into.Add(Chip.Of(Atlas.Cities[i]));
                    break;
                case Lens.Places:
                    into.Add(Chip.Globe());
                    for (var i = 0; i < City.Spots.Count && i < PlaceChips; i++) into.Add(Chip.Of(City.Spots[i], City.Spots[i] == Spot));
                    break;
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

        void Refresh()
        {
            visible.Clear();
            dates.Clear();
            foreach (var memory in Spot?.Memories ?? City?.Memories ?? all)
            {
                if (Filter != null && memory.Tier != Filter) continue;
                visible.Add(memory);
                if (dates.Count == 0 || dates[dates.Count - 1] != memory.Date) dates.Add(memory.Date);
            }
        }
    }
}
