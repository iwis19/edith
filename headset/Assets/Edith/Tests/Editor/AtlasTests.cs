using System;
using System.Linq;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class AtlasTests
    {
        const string WesternPlace = "Western University, London, Ontario, Canada";
        const string ParkPlace = "Victoria Park, London, Ontario, Canada";
        const string TowerPlace = "CN Tower, Toronto, Ontario, Canada";
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);
        // 2.8 km from Western.
        static readonly GeoPoint Park = new GeoPoint(42.9885, -81.2490);
        // 170 km from Western.
        static readonly GeoPoint Tower = new GeoPoint(43.6426, -79.3871);

        /// A memory collected on November `day`; a higher id is newer.
        static Memory Made(int id, int day, string place, GeoPoint? point, Rarity? tier = null) =>
            new Memory(id, $"Thing {id}", null, tier, place, new DateTime(2026, 11, day), point);

        /// `metres` north of `from`: a degree of latitude is 111.2 km.
        static GeoPoint North(GeoPoint from, double metres) => new GeoPoint(from.Lat + metres / 111195.0, from.Lon);

        static int[] Ids(Place place) => place.Memories.Select(memory => memory.Id).ToArray();

        /// Newest first: the tower, two at Western, one in the park, an older one at Western, and one with no place.
        static Atlas Travels() => new Atlas(new[]
        {
            Made(6, 21, TowerPlace, Tower, Rarity.Common),
            Made(5, 20, WesternPlace, Western, Rarity.Rare),
            Made(4, 20, WesternPlace, Western),
            Made(3, 19, ParkPlace, Park, Rarity.Epic),
            Made(2, 18, WesternPlace, Western, Rarity.Common),
            Made(1, 12, null, null, Rarity.Mythic),
        });

        [Test]
        public void Memories_in_one_town_are_one_city_named_after_the_town()
        {
            var atlas = Travels();
            CollectionAssert.AreEqual(new[] { "Toronto", "London" }, atlas.Cities.Select(city => city.Label).ToArray());
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2 }, Ids(atlas.Cities[1]));
            Assert.AreEqual(4, atlas.Cities[1].Count);
        }

        [Test]
        public void The_city_of_the_newest_memory_comes_first()
        {
            Assert.AreEqual("Toronto", Travels().Cities[0].Label);
            var atlas = new Atlas(new[] { Made(2, 21, WesternPlace, Western), Made(1, 20, TowerPlace, Tower) });
            Assert.AreEqual("London", atlas.Cities[0].Label);
        }

        [Test]
        public void A_memory_without_a_point_is_in_no_city()
        {
            Assert.IsFalse(Travels().Cities.Any(city => city.Memories.Any(memory => memory.Id == 1)));
            Assert.AreEqual(0, new Atlas(new[] { Made(1, 12, "Home", null) }).Cities.Count);
        }

        [Test]
        public void Places_a_short_walk_apart_are_one_spot_named_after_the_place()
        {
            var london = Travels().Cities[1];
            CollectionAssert.AreEqual(new[] { "Western University", "Victoria Park" }, london.Spots.Select(spot => spot.Label).ToArray());
            CollectionAssert.AreEqual(new[] { 5, 4, 2 }, Ids(london.Spots[0]));
            CollectionAssert.AreEqual(new[] { 3 }, Ids(london.Spots[1]));
        }

        [Test]
        public void Towns_chain_into_one_city_while_each_is_within_30_km_of_the_next()
        {
            var atlas = new Atlas(new[]
            {
                Made(3, 21, WesternPlace, Western),
                Made(2, 20, WesternPlace, North(Western, 50000)),
                Made(1, 19, WesternPlace, North(Western, 25000)),
            });
            Assert.AreEqual(1, atlas.Cities.Count);

            var apart = new Atlas(new[] { Made(2, 21, WesternPlace, Western), Made(1, 20, WesternPlace, North(Western, 31000)) });
            Assert.AreEqual(2, apart.Cities.Count);
        }

        [Test]
        public void Spots_chain_while_each_is_within_150_m_of_the_next()
        {
            var atlas = new Atlas(new[]
            {
                Made(4, 21, WesternPlace, Western),
                Made(3, 20, WesternPlace, North(Western, 240)),
                Made(2, 19, WesternPlace, North(Western, 120)),
                Made(1, 18, WesternPlace, North(Western, 400)),
            });
            var city = atlas.Cities.Single();
            Assert.AreEqual(2, city.Spots.Count);
            CollectionAssert.AreEqual(new[] { 4, 3, 2 }, Ids(city.Spots[0]));
            CollectionAssert.AreEqual(new[] { 1 }, Ids(city.Spots[1]));
        }

        [Test]
        public void A_place_of_two_parts_is_a_town_and_its_region()
        {
            var city = new Atlas(new[] { Made(1, 21, "London, Ontario", Western) }).Cities.Single();
            Assert.AreEqual("London", city.Label);
            Assert.AreEqual("London", city.Spots.Single().Label);
        }

        [Test]
        public void A_place_of_one_part_names_both_the_city_and_the_spot()
        {
            var city = new Atlas(new[] { Made(1, 21, "Home", Western) }).Cities.Single();
            Assert.AreEqual("Home", city.Label);
            Assert.AreEqual("Home", city.Spots.Single().Label);
        }

        [Test]
        public void The_centre_is_the_mean_of_the_points()
        {
            var london = Travels().Cities[1];
            Assert.AreEqual((3 * Western.Lat + Park.Lat) / 4, london.Centre.Lat, 1e-9);
            Assert.AreEqual((3 * Western.Lon + Park.Lon) / 4, london.Centre.Lon, 1e-9);
            Assert.AreEqual(Park.Lat, london.Spots[1].Centre.Lat, 1e-9);
        }

        [Test]
        public void A_city_across_the_date_line_is_centred_on_it()
        {
            var city = new Atlas(new[] { Made(2, 21, "Taveuni, Fiji", new GeoPoint(-16.8, 179.95)), Made(1, 20, "Taveuni, Fiji", new GeoPoint(-16.8, -179.95)) }).Cities.Single();
            Assert.AreEqual(180, Math.Abs(city.Centre.Lon), 1e-9);
        }

        [Test]
        public void The_best_rarity_is_the_highest_among_the_memories()
        {
            var atlas = Travels();
            Assert.AreEqual(Rarity.Epic, atlas.Cities[1].Best);
            Assert.AreEqual(Rarity.Rare, atlas.Cities[1].Spots[0].Best);
            Assert.AreEqual(Rarity.Common, atlas.Cities[0].Best);
            Assert.IsNull(new Atlas(new[] { Made(1, 21, WesternPlace, Western) }).Cities[0].Best);
        }

        [Test]
        public void The_street_map_is_centred_on_the_city_centre_rounded_to_four_places()
        {
            var city = new Atlas(new[] { Made(1, 21, WesternPlace, Western) }).Cities.Single();
            Assert.AreEqual(43.0054, city.Frame.Centre.Lat, 1e-12);
            Assert.AreEqual(-81.2748, city.Frame.Centre.Lon, 1e-12);
        }

        [Test]
        public void A_spot_is_pinned_against_the_rounded_centre_of_the_street_map()
        {
            var city = new Atlas(new[] { Made(1, 21, WesternPlace, Western) }).Cities.Single();
            Assert.AreEqual(16, city.Frame.Zoom);
            var pin = city.Frame.OnImage(city.Spots[0].Centre);
            Assert.AreEqual(512f, pin.x, 0.01f);
            // The spot is 0.00002° north of the rounded centre, which is 1.27 pixels at zoom 16.
            Assert.AreEqual(510.73f, pin.y, 0.05f);
        }

        [Test]
        public void The_street_map_zooms_out_until_every_spot_is_on_it()
        {
            var london = Travels().Cities[1];
            // The spots are 2.8 km apart and the centre is a quarter of the way along, 2.1 km from the far one.
            Assert.AreEqual(14, london.Frame.Zoom);
            foreach (var spot in london.Spots)
            {
                var at = london.Frame.OnImage(spot.Centre) - new UnityEngine.Vector2(512f, 512f);
                Assert.That(Math.Abs(at.x), Is.LessThan(0.8f * 512f), spot.Label);
                Assert.That(Math.Abs(at.y), Is.LessThan(0.8f * 512f * ScrapbookPlacement.MapVisibleShare), spot.Label);
            }
        }

        static readonly GeoPoint Sydney = new GeoPoint(-33.8688, 151.2093), Tokyo = new GeoPoint(35.6762, 139.6503),
            Lagos = new GeoPoint(6.5244, 3.3792), Lima = new GeoPoint(-12.0464, -77.0428), Oslo = new GeoPoint(59.9139, 10.7522);

        static Atlas Across(params GeoPoint[] points) =>
            new Atlas(points.Select((point, i) => Made(i + 1, 1 + i, $"Spot {i}, Town {i}, Region", point)).ToArray());

        [Test]
        public void The_world_map_is_centred_on_the_rounded_mean_of_the_city_centres()
        {
            var atlas = Travels();
            var london = atlas.Cities[1].Centre;
            var mean = new GeoPoint((atlas.Cities[0].Centre.Lat + london.Lat) / 2, (atlas.Cities[0].Centre.Lon + london.Lon) / 2);
            Assert.AreEqual(Math.Round(mean.Lat, 4), atlas.World.Centre.Lat, 1e-12);
            Assert.AreEqual(Math.Round(mean.Lon, 4), atlas.World.Centre.Lon, 1e-12);
        }

        [Test]
        public void The_world_map_shows_every_city_inside_the_visible_crop_at_a_zoom_from_2_to_12()
        {
            foreach (var cities in new[] { new[] { Sydney, Oslo }, new[] { Tokyo, Lagos, Lima }, new[] { Sydney, Tokyo, Lagos, Lima, Oslo } })
            {
                var atlas = Across(cities);
                Assert.That(atlas.World.Zoom, Is.InRange(MercatorMath.MinWorldZoom, MercatorMath.MaxWorldZoom));
                foreach (var city in atlas.Cities)
                {
                    var at = atlas.World.OnImage(city.Centre) - new UnityEngine.Vector2(512f, 512f);
                    var margin = MapFrame.Margin;
                    Assert.That(Math.Abs(at.x), Is.LessThanOrEqualTo(512f * margin), $"{city.Label} across, zoom {atlas.World.Zoom}");
                    Assert.That(Math.Abs(at.y), Is.LessThanOrEqualTo(512f * ScrapbookPlacement.MapVisibleShare * margin), $"{city.Label} down, zoom {atlas.World.Zoom}");
                }
            }
        }

        [Test]
        public void The_world_map_zooms_in_as_far_as_the_cities_allow()
        {
            var atlas = Across(Tokyo, Oslo);
            var closer = new MapFrame(atlas.World.Centre, atlas.World.Zoom + 1);
            var fits = atlas.Cities.All(city =>
            {
                var at = closer.OnImage(city.Centre) - new UnityEngine.Vector2(512f, 512f);
                return Math.Abs(at.x) <= 512f * MapFrame.Margin && Math.Abs(at.y) <= 512f * ScrapbookPlacement.MapVisibleShare * MapFrame.Margin;
            });
            Assert.IsFalse(fits, "one zoom closer, a city falls outside");
        }

        [Test]
        public void Cities_either_side_of_the_date_line_are_framed_together()
        {
            var atlas = Across(new GeoPoint(-17.7, 178.0), new GeoPoint(-13.8, -171.8));
            foreach (var city in atlas.Cities)
                Assert.That(Math.Abs(atlas.World.OnImage(city.Centre).x - 512f), Is.LessThan(512f), city.Label);
        }
    }
}
