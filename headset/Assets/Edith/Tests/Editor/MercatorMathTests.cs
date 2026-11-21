using System;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class MercatorMathTests
    {
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);

        /// `metres` north of `from`: a degree of latitude is 111.2 km.
        static GeoPoint North(GeoPoint from, double metres) => new GeoPoint(from.Lat + metres / 111195.0, from.Lon);

        static readonly Vector2 Crop = new Vector2(409.6f, 245.76f);

        static int Fit(GeoPoint[] points) => MercatorMath.ZoomToFit(Western, points, Crop, MercatorMath.MinCityZoom, MercatorMath.MaxCityZoom);

        /// `metres` east of `from` along its parallel: a degree of longitude is 111.2 km times the cosine of the latitude.
        static GeoPoint East(GeoPoint from, double metres) =>
            new GeoPoint(from.Lat, from.Lon + metres / (111195.0 * Math.Cos(from.Lat * Math.PI / 180.0)));

        [Test]
        public void The_middle_of_the_world_is_the_middle_of_the_zoom_2_image()
        {
            var (x, y) = MercatorMath.Pixel(new GeoPoint(0, 0), 2);
            Assert.AreEqual(512, x, 1e-9);
            Assert.AreEqual(512, y, 1e-9);
        }

        [Test]
        public void The_world_spans_the_whole_square()
        {
            var (left, top) = MercatorMath.Pixel(new GeoPoint(MercatorMath.MaxLatitude, -180), 2);
            var (right, bottom) = MercatorMath.Pixel(new GeoPoint(-MercatorMath.MaxLatitude, 180), 2);
            Assert.AreEqual(0, left, 1e-6);
            Assert.AreEqual(0, top, 1e-6);
            Assert.AreEqual(1024, right, 1e-6);
            Assert.AreEqual(1024, bottom, 1e-6);
        }

        [Test]
        public void A_pixel_leads_back_to_its_place()
        {
            var (x, y) = MercatorMath.Pixel(Western, 14);
            var back = MercatorMath.Point(x, y, 14);
            Assert.AreEqual(Western.Lat, back.Lat, 1e-9);
            Assert.AreEqual(Western.Lon, back.Lon, 1e-9);
        }

        [Test]
        public void London_Ontario_is_in_its_zoom_14_tile()
        {
            var (x, y) = MercatorMath.Pixel(Western, 14);
            Assert.AreEqual(4493, (int)(x / 256), "tile column");
            Assert.AreEqual(6019, (int)(y / 256), "tile row");
        }

        [Test]
        public void The_centre_of_an_area_image_is_its_middle_pixel()
        {
            var at = MercatorMath.OnImage(Western, Western, 15);
            Assert.AreEqual(512f, at.x, 1e-3f);
            Assert.AreEqual(512f, at.y, 1e-3f);
        }

        [Test]
        public void On_an_area_image_north_is_up_and_east_is_right()
        {
            var north = MercatorMath.OnImage(North(Western, 500), Western, 15);
            Assert.AreEqual(512f, north.x, 1e-3f);
            // At zoom 15 and this latitude a pixel is 3.49 m.
            Assert.AreEqual(512f - 500f / 3.494f, north.y, 0.5f);
            var east = MercatorMath.OnImage(new GeoPoint(Western.Lat, Western.Lon + 0.01), Western, 15);
            Assert.That(east.x, Is.GreaterThan(512f));
            Assert.AreEqual(512f, east.y, 1e-3f);
        }

        [Test]
        public void A_city_with_one_spot_gets_the_closest_zoom()
        {
            Assert.AreEqual(16, Fit(new[] { Western }));
        }

        [Test]
        public void The_zoom_is_the_closest_that_keeps_every_spot_within_the_crop()
        {
            // The half crop is 409.6 pixels across and 245.8 down. At this latitude 245.8 pixels are 429 m at zoom 16,
            // 859 m at zoom 15 and 1717 m at zoom 14.
            Assert.AreEqual(16, Fit(new[] { Western, North(Western, 400) }));
            Assert.AreEqual(15, Fit(new[] { Western, North(Western, 600) }));
            Assert.AreEqual(14, Fit(new[] { North(Western, -1500), North(Western, 300) }));
        }

        [Test]
        public void A_spot_far_to_the_side_counts_against_the_wider_crop()
        {
            // 409.6 pixels across are 715 m at zoom 16 and 1431 m at zoom 15.
            Assert.AreEqual(16, Fit(new[] { East(Western, 700) }));
            Assert.AreEqual(15, Fit(new[] { East(Western, 800) }));
        }

        [Test]
        public void Points_either_side_of_the_date_line_are_close_together()
        {
            var at = new GeoPoint(-16.8, 180);
            Assert.AreEqual(12, MercatorMath.ZoomToFit(at, new[] { new GeoPoint(-16.8, 179.99), new GeoPoint(-16.8, -179.99) },
                new Vector2(409.6f, 245.8f), 2, 12));
        }

        [Test]
        public void Spots_too_far_apart_for_any_zoom_get_the_widest()
        {
            Assert.AreEqual(12, Fit(new[] { North(Western, 25000) }));
        }

        [Test]
        public void Rounding_keeps_four_decimal_places()
        {
            var rounded = new GeoPoint(43.00542, -81.27487).Rounded();
            Assert.AreEqual(43.0054, rounded.Lat, 1e-12);
            Assert.AreEqual(-81.2749, rounded.Lon, 1e-12);
        }

        [Test]
        public void Distances_are_over_the_ground()
        {
            Assert.AreEqual(1000, Western.MetresTo(North(Western, 1000)), 1.0);
            // A degree of longitude at this latitude is 111.2 km times cos 43.005°.
            Assert.AreEqual(81316, Western.MetresTo(new GeoPoint(Western.Lat, Western.Lon + 1)), 50.0);
        }
    }
}
