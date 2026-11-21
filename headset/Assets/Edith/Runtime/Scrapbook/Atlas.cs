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
            Centre = Mean(memories);
            foreach (var memory in memories)
                if (memory.Tier is Rarity tier && (Best == null || tier > Best)) Best = tier;
        }

        public int Count => Memories.Count;

        static GeoPoint Mean(IReadOnlyList<Memory> memories)
        {
            var first = memories[0].Point.Value;
            double lat = 0, east = 0;
            foreach (var memory in memories)
            {
                var point = memory.Point.Value;
                lat += point.Lat;
                // Measured from the first point the short way round, so a group across longitude 180 averages there.
                var d = point.Lon - first.Lon;
                east += d > 180 ? d - 360 : d < -180 ? d + 360 : d;
            }
            var lon = first.Lon + east / memories.Count;
            return new GeoPoint(lat / memories.Count, lon > 180 ? lon - 360 : lon < -180 ? lon + 360 : lon);
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
        /// The share of the map image's half width that spots are kept within.
        const double FitShare = 0.8;

        /// Newest first, by each spot's newest memory.
        public readonly IReadOnlyList<Spot> Spots;
        /// The centre of the city's street map: GET /map/area centres its image on the rounded point.
        public readonly GeoPoint MapCentre;
        /// The tile zoom of the city's street map.
        public readonly int Zoom;

        public City(string label, IReadOnlyList<Memory> memories, IReadOnlyList<Spot> spots) : base(label, memories)
        {
            Spots = spots;
            MapCentre = Centre.Rounded();
            var points = new List<GeoPoint>(spots.Count);
            foreach (var spot in spots) points.Add(spot.Centre);
            Zoom = MercatorMath.ZoomToFit(MapCentre, points, FitShare * MercatorMath.ImagePixels / 2);
        }

        /// Where `spot` is on the city's street map image, x right and y down from its top left corner.
        public UnityEngine.Vector2 OnMap(Spot spot) => MercatorMath.OnImage(spot.Centre, MapCentre, Zoom);
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
