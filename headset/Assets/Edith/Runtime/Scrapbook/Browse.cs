using System;
using System.Collections.Generic;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// A way to reach the past. Each lens has a tab on the lens bar, in this order, and its own row of chips under it.
    public enum Lens { Places, Rarity }

    public enum ChipKind { Tab, Tier, City, Globe, Spot }

    /// One thing on the lens bar a pinch can choose. Only the field of its Kind is set.
    public readonly struct Chip
    {
        // A longer place name is cut to this many characters, so a row of place chips fits the bar.
        const int LabelLength = 18;

        public readonly ChipKind Kind;
        public readonly string Label;
        public readonly bool Selected;
        /// False for a chip that offers nothing, like a tier with no memory: it is drawn dimmed and a pinch passes through it.
        public readonly bool Enabled;
        public readonly Lens Lens;
        public readonly Rarity Tier;
        public readonly City City;
        public readonly Spot Spot;

        Chip(ChipKind kind, string label, bool selected, bool enabled = true, Lens lens = default, Rarity tier = default,
            City city = null, Spot spot = null)
        {
            Kind = kind;
            Label = label;
            Selected = selected;
            Enabled = enabled;
            Lens = lens;
            Tier = tier;
            City = city;
            Spot = spot;
        }

        public static Chip Tab(Lens lens, bool selected) => new Chip(ChipKind.Tab, lens.ToString(), selected, lens: lens);
        public static Chip Of(Rarity tier, int count, bool selected) =>
            new Chip(ChipKind.Tier, $"{tier} {count}", selected, enabled: count > 0, tier: tier);
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
        /// The most city chips, or spot chips, shown at once. The rest are reached by their pins.
        public const int PlaceChips = 4;

        static readonly Rarity[] Tiers = (Rarity[])Enum.GetValues(typeof(Rarity));

        readonly IReadOnlyList<Memory> all;
        readonly int[] counts = new int[Tiers.Length];
        readonly List<Memory> visible = new List<Memory>();

        /// `newestFirst` is kept in its order. The lens is Places when any memory has a point, else Rarity.
        public Browse(IReadOnlyList<Memory> newestFirst)
        {
            all = newestFirst;
            foreach (var memory in all)
                if (memory.Tier is Rarity tier) counts[(int)tier]++;
            Atlas = new Atlas(all);
            Lens = Atlas.Cities.Count > 0 ? Lens.Places : Lens.Rarity;
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
        /// spot. Rarity: one chip per tier, rarest last, whether or not it has a memory.
        public void Chips(List<Chip> into)
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
                case Lens.Rarity:
                    foreach (var tier in Tiers) into.Add(Chip.Of(tier, Count(tier), Filter == tier));
                    break;
            }
        }

        void Refresh()
        {
            visible.Clear();
            foreach (var memory in Spot?.Memories ?? City?.Memories ?? all)
            {
                if (Filter != null && memory.Tier != Filter) continue;
                visible.Add(memory);
            }
        }
    }
}
