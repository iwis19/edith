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
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);
        // 2.8 km from Western.
        static readonly GeoPoint Park = new GeoPoint(42.9885, -81.2490);
        // Straight through the Earth from Western, and as far east of it at Western's own latitude.
        static readonly GeoPoint Antipode = new GeoPoint(-43.00542, 98.7252), Opposite = new GeoPoint(43.00542, 98.7252);
        static readonly Vector3 Eye = Vector3.zero;

        GameObject frame;
        PlacesView places;
        CancellationTokenSource session;

        [SetUp]
        public void SetUp()
        {
            frame = new GameObject("Deck");
            places = new PlacesView(frame.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null, null, new MapImages());
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

        static Browse Home() => new Browse(new[]
        {
            Made(3, WesternPlace, Western, Rarity.Rare),
            Made(2, WesternPlace, Western),
            Made(1, "Victoria Park, London, Ontario, Canada", Park),
        });

        void Draw(Browse browse, float seconds = 0.02f) => places.Draw(browse, 1f, Eye, seconds, null, session);

        Place Pin(Vector3 through) => places.HitPin(Eye, through, out _);

        string[] Texts => frame.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();

        [Test]
        public void The_globe_opens_with_the_newest_city_facing_the_eye()
        {
            var browse = Home();
            Draw(browse);
            var london = browse.Atlas.Cities.Single();
            var pin = GlobeView.Centre + places.Spin.Rotation * MercatorMath.Direction(london.Centre) * GlobeView.Radius;
            Assert.That(Vector3.Angle(GlobeView.Centre - Eye, pin - Eye), Is.LessThan(0.5f), "the pin is in line with the globe's centre");
            Assert.That(pin.magnitude, Is.LessThan(GlobeView.Centre.magnitude), "and on the near side");
        }

        [Test]
        public void A_pinch_in_line_with_a_citys_pin_chooses_that_city()
        {
            var browse = Home();
            Draw(browse);
            Assert.AreSame(browse.Atlas.Cities.Single(), Pin(GlobeView.Centre * 0.6f));
        }

        [Test]
        public void A_pinch_away_from_every_pin_chooses_nothing()
        {
            var browse = Home();
            Draw(browse);
            Assert.IsNull(Pin((GlobeView.Centre + Vector3.right * 0.2f) * 0.6f));
            Assert.IsNull(Pin(DeckLayout.Pose(0f).Position * 0.6f));
        }

        [Test]
        public void A_city_on_the_far_side_of_the_globe_takes_no_pinch()
        {
            var browse = new Browse(new[] { Made(2, WesternPlace, Western), Made(1, "Somewhere, Far, Away", Antipode) });
            Draw(browse);
            Assert.AreEqual("London", Pin(GlobeView.Centre * 0.6f)?.Label);

            var turned = new Browse(new[] { Made(2, "Somewhere, Far, Away", Opposite), Made(1, WesternPlace, Western) });
            Draw(turned);
            Assert.AreEqual("Far", Pin(GlobeView.Centre * 0.6f)?.Label);
        }

        [Test]
        public void Dragging_the_globe_carries_a_pin_out_from_under_the_pinch()
        {
            var browse = Home();
            Draw(browse);
            Assert.IsTrue(places.GlobeSpins);
            places.Drag(new Vector2(0.03f, 0f));
            Draw(browse);
            Assert.IsNull(Pin(GlobeView.Centre * 0.6f));
        }

        [Test]
        public void After_the_dive_the_street_map_stands_where_the_globe_was_with_a_pin_for_each_spot()
        {
            var browse = Home();
            Draw(browse);
            browse.Dive(browse.Atlas.Cities.Single());
            Draw(browse, DivePose.Seconds);
            CollectionAssert.IsSubsetOf(new[] { "Western University 2", "Victoria Park 1" }, Texts);
            Assert.IsFalse(places.GlobeSpins);
            Assert.That(Vector3.Distance(CityMapView.Centre, GlobeView.Centre), Is.LessThan(0.15f));
        }

        [Test]
        public void A_pinch_in_line_with_a_spots_pin_chooses_that_spot()
        {
            var browse = new Browse(new[] { Made(1, WesternPlace, Western) });
            Draw(browse);
            browse.Dive(browse.Atlas.Cities.Single());
            Draw(browse, DivePose.Seconds);
            // The only spot is at the middle of its city's map.
            Assert.AreSame(browse.City.Spots.Single(), Pin(CityMapView.Centre * 0.6f));
            Assert.IsNull(Pin((CityMapView.Centre + Vector3.right * 0.2f) * 0.6f));
        }

        [Test]
        public void Spots_apart_in_the_city_are_apart_on_the_map_with_east_to_the_right()
        {
            var browse = Home();
            Draw(browse);
            var city = browse.Atlas.Cities.Single();
            browse.Dive(city);
            Draw(browse, DivePose.Seconds);
            var rotation = Quaternion.Euler(CityMapView.Lean, 0f, 0f);
            Vector3 On(Spot spot)
            {
                var at = CityMapView.OnDisc(city.OnMap(spot));
                return CityMapView.Centre + rotation * new Vector3(at.x, at.y, 0f);
            }
            var western = city.Spots.Single(spot => spot.Label == "Western University");
            var park = city.Spots.Single(spot => spot.Label == "Victoria Park");
            Assert.AreSame(western, Pin(On(western) * 0.6f));
            Assert.AreSame(park, Pin(On(park) * 0.6f));
            Assert.That(On(park).x, Is.GreaterThan(On(western).x), "the park is east of the university");
            Assert.That(On(park).y, Is.LessThan(On(western).y), "and south of it, so nearer the eye and lower");
            Assert.That(Vector3.Distance(On(park), CityMapView.Centre), Is.LessThan(CityMapView.Width * 0.5f));
        }

        [Test]
        public void Pins_take_no_pinch_while_the_dive_is_under_way()
        {
            var browse = Home();
            Draw(browse);
            browse.Dive(browse.Atlas.Cities.Single());
            Draw(browse, DivePose.Seconds * 0.5f);
            Assert.IsNull(Pin(GlobeView.Centre * 0.6f));
            Assert.IsNull(Pin(CityMapView.Centre * 0.6f));
            Assert.IsFalse(places.GlobeSpins);
        }

        [Test]
        public void Zooming_out_brings_back_the_globe_with_the_city_facing_the_eye()
        {
            var browse = Home();
            Draw(browse);
            places.Drag(new Vector2(0.05f, 0.01f));
            var city = browse.Atlas.Cities.Single();
            browse.Dive(city);
            Draw(browse, DivePose.Seconds);
            browse.Surface();
            Draw(browse, DivePose.Seconds);
            Assert.IsTrue(places.GlobeSpins);
            Assert.AreSame(city, Pin(GlobeView.Centre * 0.6f));
        }

        [Test]
        public void Under_another_lens_the_globe_is_gone()
        {
            var browse = Home();
            Draw(browse);
            browse.SetLens(Lens.Rarity);
            Draw(browse, 1f);
            Assert.IsFalse(places.GlobeSpins);
            Assert.IsNull(Pin(GlobeView.Centre * 0.6f));
            Assert.IsFalse(frame.GetComponentsInChildren<MeshRenderer>().Any());
        }
    }
}
