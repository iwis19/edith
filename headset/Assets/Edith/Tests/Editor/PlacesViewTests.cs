using System;
using System.Linq;
using System.Threading;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Edith.Tests
{
    /// Draws the Places lens in a deck frame at the origin, so the eye that opened the Scrapbook is at zero.
    public class PlacesViewTests
    {
        const string WesternPlace = "Western University, London, Ontario, Canada";
        const string TowerPlace = "CN Tower, Toronto, Ontario, Canada";
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);
        // 2.8 km from Western.
        static readonly GeoPoint Park = new GeoPoint(42.9885, -81.2490);
        static readonly GeoPoint Tower = new GeoPoint(43.6426, -79.3871);
        static readonly Vector3 Eye = Vector3.zero;

        GameObject frame;
        PlacesView places;
        CancellationTokenSource session;

        [SetUp]
        public void SetUp()
        {
            frame = new GameObject("Deck");
            places = new PlacesView(frame.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null, new MapImages());
            session = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(frame);
            session.Dispose();
        }

        static Memory Made(int id, string place, GeoPoint? point, Rarity? tier = null) =>
            new Memory(id, $"Thing {id}", null, tier, place, new DateTime(2026, 11, 21), point);

        /// London, with two spots, and Toronto.
        static Browse Travels() => new Browse(new[]
        {
            Made(4, TowerPlace, Tower, Rarity.Epic),
            Made(3, WesternPlace, Western, Rarity.Rare),
            Made(2, WesternPlace, Western),
            Made(1, "Victoria Park, London, Ontario, Canada", Park),
        });

        static Browse London() => new Browse(new[] { Made(3, WesternPlace, Western, Rarity.Rare), Made(2, WesternPlace, Western), Made(1, "Victoria Park, London, Ontario, Canada", Park) });

        void Draw(Browse browse, float seconds = 0.02f) => places.Draw(browse, 1f, seconds, null, session);

        Place Pin(Vector3 through) => places.HitPin(Eye, through, out _);

        /// The panel is flat, so a point `at` metres from its middle, x east and y north, is where a pin on it stands.
        static Vector3 OnPanel(Vector2 at) => ScrapbookPlacement.MapCentre + ScrapbookPlacement.MapRotation * new Vector3(at.x, at.y, 0f);

        static Vector3 Through(MapFrame mapFrame, Place place) => OnPanel(MapPanel.OnPanel(mapFrame.OnImage(place.Centre)));

        string[] Texts => frame.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();

        [Test]
        public void With_several_cities_it_opens_at_the_world_with_a_pin_for_each()
        {
            var browse = Travels();
            Draw(browse);
            CollectionAssert.IsSubsetOf(new[] { "Toronto 1", "London 3" }, Texts);
            foreach (var city in browse.Atlas.Cities)
                Assert.AreSame(city, Pin(Through(browse.Atlas.World, city) * 0.6f), city.Label);
        }

        [Test]
        public void With_one_city_it_opens_at_the_city_with_a_pin_for_each_spot()
        {
            var browse = London();
            Draw(browse);
            CollectionAssert.IsSubsetOf(new[] { "Western University 2", "Victoria Park 1" }, Texts);
            foreach (var spot in browse.City.Spots)
                Assert.AreSame(spot, Pin(Through(browse.City.Frame, spot) * 0.6f), spot.Label);
        }

        [Test]
        public void A_pinch_away_from_every_pin_chooses_nothing()
        {
            var browse = Travels();
            Draw(browse);
            Assert.IsNull(Pin((ScrapbookPlacement.MapCentre + Vector3.right * 0.3f) * 0.6f));
            Assert.IsNull(Pin(DeckLayout.Pose(0f).Position * 0.6f));
        }

        [Test]
        public void A_pin_stands_where_its_pixel_is_against_the_rounded_centre()
        {
            var browse = London();
            Draw(browse);
            var city = browse.City;
            var western = city.Spots.Single(spot => spot.Label == "Western University");
            var park = city.Spots.Single(spot => spot.Label == "Victoria Park");
            Assert.AreEqual(14, city.Frame.Zoom);
            var pixel = city.Frame.OnImage(park.Centre);
            var at = MapPanel.OnPanel(pixel);
            Assert.AreEqual((pixel.x - 512f) * ScrapbookPlacement.MapWidth / 1024f, at.x, 1e-6f);
            Assert.AreEqual((512f - pixel.y) * ScrapbookPlacement.MapWidth / 1024f, at.y, 1e-6f);
            Assert.That(at.x, Is.GreaterThan(0f), "the park is east of the centre");
            Assert.That(at.y, Is.LessThan(MapPanel.OnPanel(city.Frame.OnImage(western.Centre)).y), "and south of the university");
            Assert.AreSame(park, Pin(OnPanel(at) * 0.6f));
            Assert.AreSame(western, Pin(Through(city.Frame, western) * 0.6f));
        }

        [Test]
        public void Every_pin_stands_inside_the_panel_at_every_level()
        {
            foreach (var browse in new[] { Travels(), London() })
            {
                var mapFrame = browse.City?.Frame ?? browse.Atlas.World;
                foreach (var place in browse.City != null ? browse.City.Spots.Cast<Place>() : browse.Atlas.Cities)
                {
                    var at = MapPanel.OnPanel(mapFrame.OnImage(place.Centre));
                    Assert.That(Mathf.Abs(at.x), Is.LessThan(MapPanel.Width * 0.5f), place.Label);
                    Assert.That(Mathf.Abs(at.y), Is.LessThan(MapPanel.Height * 0.5f), place.Label);
                }
            }
        }

        [Test]
        public void The_panel_shows_the_middle_of_the_square_image_at_its_own_shape()
        {
            Draw(Travels());
            var image = frame.GetComponentInChildren<RawImage>(true);
            Assert.AreEqual(new Rect(0f, 0.2f, 1f, 0.6f), image.uvRect);
            Assert.AreEqual(5f / 3f, MapPanel.Width / MapPanel.Height, 1e-5f);
        }

        [Test]
        public void Pinching_a_city_zooms_to_its_spots_and_pins_take_no_pinch_meanwhile()
        {
            var browse = Travels();
            Draw(browse);
            var london = browse.Atlas.Cities.Single(city => city.Label == "London");
            browse.ZoomIn(london);
            Draw(browse, PlacesView.ZoomSeconds * 0.25f);
            Assert.IsNull(Pin(Through(browse.Atlas.World, london) * 0.6f), "mid zoom");
            Draw(browse, PlacesView.ZoomSeconds);
            var spot = london.Spots.Single(s => s.Label == "Victoria Park");
            Assert.AreSame(spot, Pin(Through(london.Frame, spot) * 0.6f));
            CollectionAssert.IsSubsetOf(new[] { "Western University 2", "Victoria Park 1" }, Texts);
        }

        [Test]
        public void The_world_chip_zooms_back_out_to_the_cities()
        {
            var browse = Travels();
            Draw(browse);
            var london = browse.Atlas.Cities.Single(city => city.Label == "London");
            browse.ZoomIn(london);
            Draw(browse, PlacesView.ZoomSeconds);
            Draw(browse, PlacesView.ZoomSeconds);
            browse.ZoomOut();
            Draw(browse, PlacesView.ZoomSeconds * 0.25f);
            Assert.IsNull(Pin(Through(london.Frame, london.Spots[0]) * 0.6f), "mid zoom");
            Draw(browse, PlacesView.ZoomSeconds);
            Draw(browse, PlacesView.ZoomSeconds);
            Assert.AreSame(london, Pin(Through(browse.Atlas.World, london) * 0.6f));
        }

        [Test]
        public void Under_another_lens_the_map_is_gone()
        {
            var browse = Travels();
            Draw(browse);
            browse.SetLens(Lens.Rarity);
            Draw(browse, 1f);
            Assert.IsNull(Pin(Through(browse.Atlas.World, browse.Atlas.Cities[0]) * 0.6f));
            Assert.IsFalse(frame.GetComponentsInChildren<Graphic>().Any(graphic => graphic.gameObject.activeInHierarchy));
        }
    }
}
