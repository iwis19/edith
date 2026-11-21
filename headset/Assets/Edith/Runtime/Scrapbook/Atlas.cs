using System;
using System.Collections.Generic;
using Edith.Look;

namespace Edith.Scrapbook
{
    /// Somewhere memories were collected: a city, or a spot inside one.
    public abstract class Place
    {
        public readonly string Label;
        /// The mean of its memories' points.
        public readonly GeoPoint Centre;
        /// Newest first.
        public readonly IReadOnlyList<Memory> Memories;
        /// The highest rarity among its memories, or null when none has one.
        public readonly Rarity? Best;

        protected Place(string label, IReadOnlyList<Memory> memories)
        {
            Label = label;
            Memories = memories;
            Centre = GeoPoint.Mean(Points(memories));
            foreach (var memory in memories)
                if (memory.Tier is Rarity tier && (Best == null || tier > Best)) Best = tier;
        }

        public int Count => Memories.Count;

        static List<GeoPoint> Points(IReadOnlyList<Memory> memories)
        {
            var points = new List<GeoPoint>(memories.Count);
            foreach (var memory in memories) points.Add(memory.Point.Value);
            return points;
        }
    }

    /// Memories collected within a short walk of each other. Its label is the first part of their place.
    public sealed class Spot : Place
    {
        public Spot(string label, IReadOnlyList<Memory> memories) : base(label, memories) { }
    }

    /// Memories collected in one town. Its label is the town's part of their place.
    public sealed class City : Place
    {
        /// Newest first, by each spot's newest memory.
        public readonly IReadOnlyList<Spot> Spots;
        /// The city's street map, framed to hold every spot.
        public readonly MapFrame Frame;

        public City(string label, IReadOnlyList<Memory> memories, IReadOnlyList<Spot> spots) : base(label, memories)
        {
            Spots = spots;
            var points = new List<GeoPoint>(spots.Count);
            foreach (var spot in spots) points.Add(spot.Centre);
            Frame = MapFrame.Fit(Centre, points, MercatorMath.MinCityZoom, MercatorMath.MaxCityZoom);
        }
    }

    /// The memories that have a point, grouped by where they were collected.
    public sealed class Atlas
    {
        /// Memories chain into one city while each is within this of another.
        public const double CityMetres = 30000;
        /// Memories chain into one spot while each is within this of another.
        public const double SpotMetres = 150;
        const string Unnamed = "Unnamed";

        /// Newest first, by each city's newest memory.
        public readonly IReadOnlyList<City> Cities;
        /// The map of every city at once, or default when there is none.
        public readonly MapFrame World;

        public Atlas(IReadOnlyList<Memory> newestFirst)
        {
            var located = new List<Memory>();
            foreach (var memory in newestFirst)
                if (memory.Point != null) located.Add(memory);

            var cities = new List<City>();
            foreach (var town in Linked(located, CityMetres))
            {
                var spots = new List<Spot>();
                foreach (var walk in Linked(town, SpotMetres)) spots.Add(new Spot(Part(walk, false), walk));
                cities.Add(new City(Part(town, true), town, spots));
            }
            Cities = cities;
            if (cities.Count == 0) return;
            var centres = new List<GeoPoint>(cities.Count);
            foreach (var city in cities) centres.Add(city.Centre);
            World = MapFrame.Fit(GeoPoint.Mean(centres), centres, MercatorMath.MinWorldZoom, MercatorMath.MaxWorldZoom);
        }

        /// The part of the newest named place among `memories` that names a town or a spot.
        static string Part(List<Memory> memories, bool town)
        {
            foreach (var memory in memories)
                if (memory.Place != null) return Part(memory.Place, town);
            return Unnamed;
        }

        /// The part of `place` that names a town or a spot. A place reads "spot, town, region, country", or
        /// "town, region" when it has no spot.
        public static string Part(string place, bool town)
        {
            var parts = place.Split(new[] { ", " }, StringSplitOptions.None);
            return town && parts.Length >= 3 ? parts[1] : parts[0];
        }

        /// Single-link groups: two memories share a group when a chain of memories joins them, each within `metres`
        /// of the next. Groups and their members keep the order of `memories`.
        static List<List<Memory>> Linked(List<Memory> memories, double metres)
        {
            var group = new int[memories.Count];
            for (var i = 0; i < group.Length; i++) group[i] = i;
            for (var i = 0; i < memories.Count; i++)
                for (var j = i + 1; j < memories.Count; j++)
                {
                    if (group[i] == group[j] || memories[i].Point.Value.MetresTo(memories[j].Point.Value) > metres) continue;
                    int keep = Math.Min(group[i], group[j]), drop = Math.Max(group[i], group[j]);
                    for (var k = 0; k < group.Length; k++)
                        if (group[k] == drop) group[k] = keep;
                }

            var groups = new List<List<Memory>>();
            var at = new Dictionary<int, List<Memory>>();
            for (var i = 0; i < memories.Count; i++)
            {
                if (!at.TryGetValue(group[i], out var members))
                {
                    at[group[i]] = members = new List<Memory>();
                    groups.Add(members);
                }
                members.Add(memories[i]);
            }
            return groups;
        }
    }
}
