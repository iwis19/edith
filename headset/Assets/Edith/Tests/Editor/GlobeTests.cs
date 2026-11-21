using System;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Edith.Tests
{
    public class GlobeTests
    {
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);
        static readonly Vector3 Eye = Vector3.zero;

        /// The direction from the globe's centre to an eye `elevation` degrees above it, straight in front.
        static Vector3 ToEye(float elevation) =>
            new Vector3(0f, Mathf.Sin(elevation * Mathf.Deg2Rad), -Mathf.Cos(elevation * Mathf.Deg2Rad));

        static Vector3 Turned(GlobeSpin spin, GeoPoint point) => spin.Rotation * MercatorMath.Direction(point);

        [Test]
        public void Facing_a_place_turns_it_to_the_eye()
        {
            var spin = GlobeSpin.Facing(Western, GlobeView.EyeElevation);
            Assert.That(Vector3.Angle(ToEye(GlobeView.EyeElevation), Turned(spin, Western)), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(ToEye(10f), Turned(GlobeSpin.Facing(new GeoPoint(-20, 150), 10f), new GeoPoint(-20, 150))), Is.LessThan(0.01f));
        }

        [Test]
        public void The_eye_that_opened_the_scrapbook_looks_down_on_the_globe()
        {
            Assert.That(Vector3.Angle(ToEye(GlobeView.EyeElevation), Eye - GlobeView.Centre), Is.LessThan(0.01f));
        }

        [Test]
        public void The_globe_never_tips_past_its_limit()
        {
            Assert.AreEqual(GlobeSpin.MaxTilt, GlobeSpin.Facing(new GeoPoint(-70, 0), 40f).Tilt);
            Assert.AreEqual(-GlobeSpin.MaxTilt, GlobeSpin.Facing(new GeoPoint(80, 0), 0f).Tilt);
            var spin = default(GlobeSpin);
            for (var i = 0; i < 50; i++) spin = spin.Dragged(new Vector2(0f, 0.05f), GlobeView.Radius);
            Assert.AreEqual(GlobeSpin.MaxTilt, spin.Tilt);
        }

        [Test]
        public void A_drag_to_the_right_carries_the_surface_to_the_right()
        {
            var spin = GlobeSpin.Facing(Western, GlobeView.EyeElevation);
            var before = Turned(spin, Western);
            var after = Turned(spin.Dragged(new Vector2(0.01f, 0f), GlobeView.Radius), Western);
            Assert.That(after.x, Is.GreaterThan(before.x + 0.05f));
            Assert.AreEqual(spin.Tilt, spin.Dragged(new Vector2(0.01f, 0f), GlobeView.Radius).Tilt);
        }

        [Test]
        public void A_drag_up_carries_the_surface_up()
        {
            var spin = GlobeSpin.Facing(Western, GlobeView.EyeElevation);
            var after = Turned(spin.Dragged(new Vector2(0f, 0.01f), GlobeView.Radius), Western);
            Assert.That(after.y, Is.GreaterThan(Turned(spin, Western).y + 0.05f));
            Assert.AreEqual(0f, after.x, 1e-4f);
        }

        [Test]
        public void A_spin_about_the_axis_leaves_the_north_pole_where_it_is()
        {
            var spin = new GlobeSpin(0f, 20f);
            var pole = Turned(spin, new GeoPoint(90, 0));
            Assert.That(Vector3.Distance(pole, Turned(spin.Dragged(new Vector2(0.03f, 0f), GlobeView.Radius), new GeoPoint(90, 0))), Is.LessThan(1e-5f));
        }

        [Test]
        public void Turning_between_two_spins_goes_the_short_way_round()
        {
            var half = GlobeSpin.Lerp(new GlobeSpin(170f, 0f), new GlobeSpin(-170f, 40f), 0.5f);
            Assert.AreEqual(180f, Mathf.Abs(half.Yaw), 1e-3f);
            Assert.AreEqual(20f, half.Tilt, 1e-3f);
        }

        [Test]
        public void The_dive_starts_at_the_globe_and_ends_in_the_map()
        {
            var start = DivePose.At(0f);
            Assert.AreEqual(1f, start.GlobeAlpha);
            Assert.AreEqual(1f, start.GlobeScale);
            Assert.AreEqual(0f, start.MapAlpha);
            Assert.AreEqual(0f, start.Turn);
            var end = DivePose.At(1f);
            Assert.AreEqual(0f, end.GlobeAlpha);
            Assert.AreEqual(1.6f, end.GlobeScale, 1e-5f);
            Assert.AreEqual(1f, end.MapAlpha);
            Assert.AreEqual(1f, end.MapScale);
        }

        [Test]
        public void The_city_faces_the_eye_before_the_map_appears()
        {
            for (var progress = 0f; progress <= 1f; progress += 0.05f)
            {
                var pose = DivePose.At(progress);
                if (pose.MapAlpha > 0f) Assert.AreEqual(1f, pose.Turn, $"at {progress}");
            }
        }

        [Test]
        public void The_globe_swells_and_fades_as_the_map_grows_in()
        {
            var last = DivePose.At(0f);
            for (var progress = 0.05f; progress <= 1f; progress += 0.05f)
            {
                var pose = DivePose.At(progress);
                Assert.That(pose.GlobeScale, Is.GreaterThanOrEqualTo(last.GlobeScale));
                Assert.That(pose.GlobeAlpha, Is.LessThanOrEqualTo(last.GlobeAlpha));
                Assert.That(pose.MapAlpha, Is.GreaterThanOrEqualTo(last.MapAlpha));
                Assert.That(pose.MapScale, Is.GreaterThanOrEqualTo(last.MapScale));
                last = pose;
            }
        }

        [Test]
        public void A_pin_on_the_side_toward_the_eye_is_seen()
        {
            var toEye = (Eye - GlobeView.Centre).normalized;
            Assert.IsTrue(PinchRoute.FacesEye(GlobeView.Centre, GlobeView.Centre + toEye * GlobeView.Radius, Eye));
            var aside = Quaternion.AngleAxis(60f, Vector3.right) * toEye;
            Assert.IsTrue(PinchRoute.FacesEye(GlobeView.Centre, GlobeView.Centre + aside * GlobeView.Radius, Eye));
        }

        [Test]
        public void A_pin_on_the_far_side_or_on_the_edge_is_hidden()
        {
            var toEye = (Eye - GlobeView.Centre).normalized;
            Assert.IsFalse(PinchRoute.FacesEye(GlobeView.Centre, GlobeView.Centre - toEye * GlobeView.Radius, Eye));
            var edge = Quaternion.AngleAxis(85f, Vector3.right) * toEye;
            Assert.IsFalse(PinchRoute.FacesEye(GlobeView.Centre, GlobeView.Centre + edge * GlobeView.Radius, Eye));
            var behind = Quaternion.AngleAxis(-120f, Vector3.right) * toEye;
            Assert.IsFalse(PinchRoute.FacesEye(GlobeView.Centre, GlobeView.Centre + behind * GlobeView.Radius, Eye));
        }

        /// A point `degrees` to the side of `target`, which is level with the eye, and as far from the eye.
        static Vector3 Off(Vector3 target, float degrees) => Quaternion.AngleAxis(degrees, Vector3.up) * target;

        [Test]
        public void A_tap_within_six_degrees_of_a_pin_chooses_it()
        {
            var pin = new Vector3(0f, 0f, 0.5f);
            Assert.AreEqual(0, PinchRoute.NearestPin(Eye, Off(pin, 5.5f) * 0.6f, new[] { pin }, out var off));
            Assert.AreEqual(5.5f, off, 0.01f);
            Assert.AreEqual(-1, PinchRoute.NearestPin(Eye, Off(pin, 6.5f) * 0.6f, new[] { pin }, out _));
            Assert.AreEqual(-1, PinchRoute.NearestPin(Eye, pin, Array.Empty<Vector3>(), out _));
        }

        [Test]
        public void Between_two_pins_a_tap_chooses_the_nearer()
        {
            var pin = new Vector3(0f, 0f, 0.5f);
            var pins = new[] { Off(pin, -1.5f), Off(pin, 1f) };
            Assert.AreEqual(1, PinchRoute.NearestPin(Eye, pin * 0.6f, pins, out _));
            Assert.AreEqual(0, PinchRoute.NearestPin(Eye, Off(pin, -1f) * 0.6f, pins, out _));
        }

        [Test]
        public void A_drag_that_starts_on_the_globe_spins_it()
        {
            var onGlobe = (GlobeView.Centre + Vector3.right * GlobeView.Radius * 0.8f) * 0.6f;
            Assert.AreEqual(DragTarget.Globe, PinchRoute.DragStart(true, Eye, onGlobe, GlobeView.Centre, GlobeView.Radius));
            Assert.AreEqual(DragTarget.Globe, PinchRoute.DragStart(true, Eye, GlobeView.Centre * 0.6f, GlobeView.Centre, GlobeView.Radius));
        }

        [Test]
        public void A_drag_that_starts_off_the_globe_scrubs_the_deck()
        {
            var beside = (GlobeView.Centre + Vector3.right * GlobeView.Radius * 1.3f) * 0.6f;
            Assert.AreEqual(DragTarget.Deck, PinchRoute.DragStart(true, Eye, beside, GlobeView.Centre, GlobeView.Radius));
            var onTheDeck = DeckLayout.Pose(0f).Position * 0.6f;
            Assert.AreEqual(DragTarget.Deck, PinchRoute.DragStart(true, Eye, onTheDeck, GlobeView.Centre, GlobeView.Radius));
            Assert.AreEqual(DragTarget.Deck, PinchRoute.DragStart(true, Eye, -GlobeView.Centre, GlobeView.Centre, GlobeView.Radius), "behind the eye");
        }

        [Test]
        public void A_drag_on_a_globe_that_cannot_spin_scrubs_the_deck()
        {
            Assert.AreEqual(DragTarget.Deck, PinchRoute.DragStart(false, Eye, GlobeView.Centre * 0.6f, GlobeView.Centre, GlobeView.Radius));
        }

        [Test]
        public void The_globes_faces_point_outward()
        {
            var mesh = MapMeshes.Globe();
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                Assert.That(triangles.Length, Is.GreaterThan(0));
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    // Unity's front face is the one whose corners run clockwise, whose normal is this cross product.
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c), Is.GreaterThan(0f), $"triangle {i / 3}");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Every_point_of_the_globe_shows_its_own_place_on_the_world_image()
        {
            var mesh = MapMeshes.Globe();
            try
            {
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var at = vertices[i];
                    Assert.AreEqual(1f, at.magnitude, 1e-5f);
                    var latitude = Mathf.Asin(Mathf.Clamp(at.y, -1f, 1f)) * Mathf.Rad2Deg;
                    Assert.AreEqual(MercatorMath.UV(new GeoPoint(latitude, 0)).y, uvs[i].y, 2e-4f, $"v at latitude {latitude}");
                    if (Mathf.Abs(latitude) > 89f) continue;
                    var longitude = Mathf.Atan2(at.x, -at.z) * Mathf.Rad2Deg;
                    var u = MercatorMath.UV(new GeoPoint(0, longitude)).x;
                    // Longitude 180 is both edges of the image.
                    Assert.That(Mathf.Min(Mathf.Abs(u - uvs[i].x), 1f - Mathf.Abs(u - uvs[i].x)), Is.LessThan(2e-4f), $"u at longitude {longitude}");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void The_street_map_is_a_disc_showing_the_circle_in_the_middle_of_its_image()
        {
            var mesh = MapMeshes.Disc();
            try
            {
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                for (var i = 0; i < vertices.Length; i++)
                {
                    Assert.That(((Vector2)vertices[i]).magnitude, Is.LessThanOrEqualTo(0.5f + 1e-5f));
                    Assert.AreEqual(0f, vertices[i].z);
                    Assert.That(Vector2.Distance(uvs[i], (Vector2)vertices[i] + new Vector2(0.5f, 0.5f)), Is.LessThan(1e-5f));
                }
                var triangles = mesh.triangles;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Assert.That(Vector3.Cross(b - a, c - a).z, Is.LessThan(0f), "faces the viewer in front of it");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
