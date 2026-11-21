using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class ScrapbookPlacementTests
    {
        const float MinMeters = 0.6f;

        static float Elevation(Vector3 at) => Mathf.Atan2(-at.y, at.z) * Mathf.Rad2Deg;

        [Test]
        public void The_middle_card_is_centred_a_little_below_the_eye_and_a_half_again_as_big()
        {
            Assert.AreEqual(new Vector3(0f, -0.04f, 0.70f), ScrapbookPlacement.CardCentre);
            Assert.AreEqual(0.30f, DeckLayout.CardWidth, 1e-6f);
            Assert.AreEqual(0.225f, DeckLayout.CardHeight, 1e-6f);
        }

        [Test]
        public void The_cards_look_about_as_wide_as_they_did_nearer()
        {
            var before = 0.20f / 0.45f;
            var now = DeckLayout.CardWidth / ScrapbookPlacement.CardCentre.z;
            Assert.That(now, Is.EqualTo(before).Within(before * 0.1f));
        }

        [Test]
        public void Every_card_is_far_enough_from_the_eye()
        {
            for (var offset = -3f; offset <= 3f; offset += 0.25f)
            {
                var pose = DeckLayout.Pose(offset);
                var half = pose.Rotation * Vector3.right * (DeckLayout.CardWidth * pose.Scale * 0.5f);
                Assert.That((pose.Position - half).magnitude, Is.GreaterThanOrEqualTo(MinMeters), $"left edge of the card at {offset}");
                Assert.That((pose.Position + half).magnitude, Is.GreaterThanOrEqualTo(MinMeters), $"right edge of the card at {offset}");
            }
        }

        [Test]
        public void The_side_cards_fan_out_a_half_again_as_far()
        {
            Assert.AreEqual(0.24f, DeckLayout.Pose(1f).Position.x, 1e-5f);
            Assert.AreEqual(0.345f, DeckLayout.Pose(2f).Position.x, 1e-5f);
        }

        [Test]
        public void The_lens_bar_is_about_19_degrees_below_the_eye_at_0_66_m_out()
        {
            Assert.AreEqual(0.66f, ScrapbookPlacement.BarCentre.z, 1e-6f);
            Assert.AreEqual(19f, Elevation(ScrapbookPlacement.BarCentre), 0.01f);
            Assert.That(ScrapbookPlacement.BarCentre.magnitude, Is.GreaterThanOrEqualTo(MinMeters));
        }

        [Test]
        public void The_globe_is_about_30_degrees_below_the_eye_at_0_70_m_with_a_radius_of_0_12_m()
        {
            Assert.AreEqual(0.70f, GlobeView.Centre.z, 1e-6f);
            Assert.AreEqual(30f, Elevation(GlobeView.Centre), 0.01f);
            Assert.AreEqual(0.12f, GlobeView.Radius);
            Assert.That(GlobeView.Centre.magnitude - GlobeView.Radius, Is.GreaterThanOrEqualTo(MinMeters));
            Assert.AreEqual(30f, GlobeView.EyeElevation, 0.01f);
        }

        [Test]
        public void The_city_map_is_about_32_degrees_below_the_eye_at_0_78_m_and_leans_back_40_degrees()
        {
            Assert.AreEqual(0.78f, CityMapView.Centre.z, 1e-6f);
            Assert.AreEqual(32f, Elevation(CityMapView.Centre), 0.01f);
            Assert.AreEqual(0.42f, CityMapView.Width);
            Assert.AreEqual(40f, CityMapView.Lean);
            Assert.That(CityMapView.Centre.magnitude - CityMapView.Width * 0.5f, Is.GreaterThanOrEqualTo(MinMeters));
        }

        [Test]
        public void The_angles_are_gentler_than_the_globe_and_map_at_arms_length_were()
        {
            Assert.That(Elevation(GlobeView.Centre), Is.LessThan(39f));
            Assert.That(Elevation(CityMapView.Centre), Is.LessThan(39f));
            Assert.That(CityMapView.Lean, Is.LessThan(50f));
        }

        [Test]
        public void The_globe_and_the_map_look_about_as_big_as_they_did_nearer()
        {
            Assert.That(GlobeView.Radius / GlobeView.Centre.magnitude, Is.EqualTo(0.085f / 0.54f).Within(0.02f));
            Assert.That(CityMapView.Width / CityMapView.Centre.magnitude, Is.EqualTo(0.30f / 0.57f).Within(0.08f));
        }
    }
}
