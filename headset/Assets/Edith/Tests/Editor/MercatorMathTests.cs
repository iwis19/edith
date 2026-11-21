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
        public void The_world_image_runs_from_its_last_latitude_south_to_its_last_latitude_north()
        {
            Assert.AreEqual(1f, MercatorMath.UV(new GeoPoint(MercatorMath.MaxLatitude, 0)).y, 1e-6f);
            Assert.AreEqual(0f, MercatorMath.UV(new GeoPoint(-MercatorMath.MaxLatitude, 0)).y, 1e-6f);
            Assert.AreEqual(0.5f, MercatorMath.UV(new GeoPoint(0, 0)).y, 1e-6f);
        }

        [Test]
        public void The_poles_take_the_world_images_edge_rows()
        {
            Assert.AreEqual(1f, MercatorMath.UV(new GeoPoint(90, 0)).y, 1e-6f);
            Assert.AreEqual(0f, MercatorMath.UV(new GeoPoint(-90, 0)).y, 1e-6f);
        }

        [Test]
        public void The_world_image_runs_east_from_the_date_line()
        {
            Assert.AreEqual(0f, MercatorMath.UV(new GeoPoint(0, -180)).x, 1e-6f);
            Assert.AreEqual(0.5f, MercatorMath.UV(new GeoPoint(0, 0)).x, 1e-6f);
            Assert.AreEqual(0.75f, MercatorMath.UV(new GeoPoint(0, 90)).x, 1e-6f);
        }

        [Test]
        public void On_the_globe_north_is_up_and_east_is_to_the_right_of_the_meridian_facing_the_viewer()
        {
            Assert.That(Vector3.Distance(Vector3.up, MercatorMath.Direction(new GeoPoint(90, 0))), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(Vector3.back, MercatorMath.Direction(new GeoPoint(0, 0))), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(Vector3.right, MercatorMath.Direction(new GeoPoint(0, 90))), Is.LessThan(1e-6f));
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
            Assert.AreEqual(16, MercatorMath.ZoomToFit(Western, new[] { Western }, 409.6));
        }

        [Test]
        public void The_zoom_is_the_closest_that_keeps_every_spot_within_the_radius()
        {
            // 409.6 pixels at this latitude are 715 m at zoom 16, 1431 m at zoom 15 and 2862 m at zoom 14.
            Assert.AreEqual(16, MercatorMath.ZoomToFit(Western, new[] { Western, North(Western, 600) }, 409.6));
            Assert.AreEqual(15, MercatorMath.ZoomToFit(Western, new[] { Western, North(Western, 1000) }, 409.6));
            Assert.AreEqual(14, MercatorMath.ZoomToFit(Western, new[] { North(Western, -2000), North(Western, 300) }, 409.6));
        }

        [Test]
        public void Spots_too_far_apart_for_any_zoom_get_the_widest()
        {
            Assert.AreEqual(12, MercatorMath.ZoomToFit(Western, new[] { North(Western, 25000) }, 409.6));
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
