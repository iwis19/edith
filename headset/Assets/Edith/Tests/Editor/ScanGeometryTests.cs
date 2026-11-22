using System;
using Edith.Capture;
using Edith.Scan;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class ScanReplyTests
    {
        [Test]
        public void A_reply_parses_into_picks_in_order()
        {
            var picks = ScanReply.Parse(
                "{\"scan_ref\":\"ab\",\"ms\":1200,\"picks\":[" +
                "{\"name\":\"a brass compass\",\"hook\":\"Spot which way it points.\",\"point\":[0.25,0.4],\"box\":[0.2,0.3,0.4,0.6]}," +
                "{\"name\":\"a globe\",\"hook\":\"Spin it once.\",\"point\":[0.7,0.5],\"box\":[0.6,0.4,0.8,0.7]}]}");
            Assert.AreEqual(2, picks.Count);
            Assert.AreEqual("a brass compass", picks[0].Name);
            Assert.AreEqual("Spot which way it points.", picks[0].Hook);
            Assert.AreEqual(new Vector2(0.25f, 0.4f), picks[0].ImagePoint);
            Assert.AreEqual(Rect.MinMaxRect(0.2f, 0.3f, 0.4f, 0.6f), picks[0].ImageBox);
            Assert.AreEqual("a globe", picks[1].Name);
        }

        [Test]
        public void Image_coordinates_flip_into_the_viewport()
        {
            var pick = new ScanPick("a lamp", "Switch it.", new Vector2(0.25f, 0.1f), Rect.MinMaxRect(0.2f, 0.05f, 0.4f, 0.3f));
            Assert.AreEqual(0.25f, pick.ViewportPoint.x, 1e-6f);
            Assert.AreEqual(0.9f, pick.ViewportPoint.y, 1e-6f);
            Assert.AreEqual(0.7f, pick.ViewportBox.yMin, 1e-6f);
            Assert.AreEqual(0.95f, pick.ViewportBox.yMax, 1e-6f);
            Assert.AreEqual(0.2f, pick.ViewportBox.xMin, 1e-6f);
        }

        [Test]
        public void No_picks_is_an_empty_list()
        {
            Assert.AreEqual(0, ScanReply.Parse("{\"scan_ref\":\"ab\",\"ms\":900,\"picks\":[]}").Count);
            Assert.AreEqual(0, ScanReply.Parse("{}").Count);
        }

        [Test]
        public void Picks_without_a_point_or_with_an_empty_box_are_left_out()
        {
            var picks = ScanReply.Parse(
                "{\"picks\":[{\"name\":\"no point\",\"hook\":\"x\",\"box\":[0.1,0.1,0.2,0.2]}," +
                "{\"name\":\"flat\",\"hook\":\"x\",\"point\":[0.1,0.1],\"box\":[0.1,0.1,0.1,0.2]}," +
                "{\"name\":\"good\",\"hook\":\"x\",\"point\":[0.1,0.1],\"box\":[0.0,0.0,0.2,0.2]}]}");
            Assert.AreEqual(1, picks.Count);
            Assert.AreEqual("good", picks[0].Name);
        }

        [Test]
        public void Text_that_is_not_json_throws()
        {
            Assert.Throws<ArgumentException>(() => ScanReply.Parse("<html>sign in</html>"));
        }

        [Test]
        public void The_meta_field_carries_a_utc_time_and_the_session()
        {
            var at = new DateTime(2026, 11, 21, 10, 0, 5, 250, DateTimeKind.Utc);
            Assert.AreEqual("{\"captured_at\":\"2026-11-21T10:00:05.250Z\",\"session\":\"abc\",\"interests\":[]}", ScanClient.Meta(at, "abc"));
        }

        [Test]
        public void The_meta_field_carries_the_interests()
        {
            var at = new DateTime(2026, 11, 21, 10, 0, 5, 250, DateTimeKind.Utc);
            Assert.AreEqual("{\"captured_at\":\"2026-11-21T10:00:05.250Z\",\"session\":\"abc\",\"interests\":[\"Art\",\"Cars\"]}",
                ScanClient.Meta(at, "abc", new[] { "Art", "Cars" }));
        }
    }

    public class ScanDepthGridTests
    {
        [Test]
        public void A_cell_reads_back_at_its_own_viewport_point()
        {
            var grid = new ScanDepthGrid(16, 12);
            Assert.IsNull(grid.At(grid.Viewport(37)));
            grid.Record(37, 2.5f);
            Assert.AreEqual(2.5f, grid.At(grid.Viewport(37)));
            Assert.IsNull(grid.At(grid.Viewport(38)));
            Assert.AreEqual(1, grid.Known);
            Assert.AreEqual(192, grid.Count);
        }

        [Test]
        public void A_cell_keeps_the_farthest_depth_it_was_given()
        {
            var grid = new ScanDepthGrid(4, 4);
            grid.Record(5, 0.5f);
            grid.Record(5, 3f);
            grid.Record(5, 1f);
            Assert.AreEqual(3f, grid.At(grid.Viewport(5)));
        }

        [Test]
        public void A_cell_that_met_nothing_reads_as_nothing_until_it_gets_a_depth()
        {
            var grid = new ScanDepthGrid(4, 4);
            Assert.IsFalse(grid.NothingAt(grid.Viewport(6)));
            grid.RecordNothing(6);
            Assert.IsTrue(grid.NothingAt(grid.Viewport(6)));
            Assert.IsNull(grid.At(grid.Viewport(6)));
            grid.Record(6, 2f);
            Assert.IsFalse(grid.NothingAt(grid.Viewport(6)));
        }

        [Test]
        public void Points_on_the_edge_of_the_frame_fall_in_the_edge_cells()
        {
            var grid = new ScanDepthGrid(4, 4);
            grid.Record(15, 2f);
            Assert.AreEqual(2f, grid.At(new Vector2(1f, 1f)));
            grid.Record(0, 1f);
            Assert.AreEqual(1f, grid.At(new Vector2(-0.1f, 0f)));
        }
    }

    public class ScanGeometryTests
    {
        static readonly ScanTuning Tuning = new ScanTuning();

        [Test]
        public void A_depth_hit_places_the_pin_where_it_was_measured()
        {
            Assert.AreEqual((2.4f, PinSource.Measured), ScanGeometry.PinDistance(DepthAnswer.Hit, 2.4f, true, 3f, false, 1f, Tuning));
        }

        [Test]
        public void A_hit_on_a_raised_hand_falls_back_to_the_remembered_depth_along_the_line()
        {
            var cos = Mathf.Cos(30f * Mathf.Deg2Rad);
            var (meters, source) = ScanGeometry.PinDistance(DepthAnswer.Hit, 0.2f, true, 2f, false, cos, Tuning);
            Assert.AreEqual(PinSource.Remembered, source);
            Assert.AreEqual(2f / cos, meters, 1e-4f);
        }

        [Test]
        public void A_hit_on_a_raised_hand_with_nothing_remembered_is_assumed_not_far()
        {
            Assert.AreEqual((Tuning.assumedPinMeters, PinSource.Assumed), ScanGeometry.PinDistance(DepthAnswer.Hit, 0.2f, true, null, false, 1f, Tuning));
        }

        [Test]
        public void A_line_that_meets_nothing_gets_a_far_pin()
        {
            Assert.AreEqual((Tuning.farPinMeters, PinSource.Far), ScanGeometry.PinDistance(DepthAnswer.Nothing, 0f, true, null, false, 1f, Tuning));
        }

        [Test]
        public void A_line_that_meets_nothing_now_still_uses_what_was_remembered()
        {
            Assert.AreEqual((1.8f, PinSource.Remembered), ScanGeometry.PinDistance(DepthAnswer.Nothing, 0f, true, 1.8f, false, 1f, Tuning));
        }

        [Test]
        public void A_hit_read_after_the_head_moved_gives_way_to_the_depth_remembered_at_capture()
        {
            // Turned away, depth no longer sees a glasses case 0.6 m out and meets the bag 2.4 m behind it instead.
            Assert.AreEqual((0.6f, PinSource.Remembered),
                ScanGeometry.PinDistance(DepthAnswer.Hit, 2.4f, false, 0.6f, false, 1f, Tuning));
        }

        [Test]
        public void A_hit_read_after_the_head_moved_with_nothing_remembered_is_assumed()
        {
            Assert.AreEqual((Tuning.assumedPinMeters, PinSource.Assumed),
                ScanGeometry.PinDistance(DepthAnswer.Hit, 2.4f, false, null, false, 1f, Tuning));
        }

        [Test]
        public void A_line_remembered_meeting_nothing_gets_a_far_pin_after_the_head_moved()
        {
            Assert.AreEqual((Tuning.farPinMeters, PinSource.Far),
                ScanGeometry.PinDistance(DepthAnswer.Hit, 1.2f, false, null, true, 1f, Tuning));
        }

        [Test]
        public void The_camera_holds_still_within_the_angle_and_distance()
        {
            var capture = new Pose(Vector3.zero, Quaternion.identity);
            Assert.IsTrue(ScanGeometry.HeldStill(capture, new Pose(new Vector3(0.05f, 0f, 0f), Quaternion.Euler(0f, 4f, 0f)), Tuning));
            Assert.IsFalse(ScanGeometry.HeldStill(capture, new Pose(Vector3.zero, Quaternion.Euler(0f, 6f, 0f)), Tuning));
            Assert.IsFalse(ScanGeometry.HeldStill(capture, new Pose(new Vector3(0f, 0f, 0.15f), Quaternion.identity), Tuning));
        }

        [Test]
        public void No_depth_at_all_assumes_a_distance()
        {
            Assert.AreEqual((Tuning.assumedPinMeters, PinSource.Assumed), ScanGeometry.PinDistance(DepthAnswer.None, 0f, true, null, false, 1f, Tuning));
        }

        [Test]
        public void The_nearest_point_within_the_angle_wins()
        {
            var line = new Ray(Vector3.zero, Vector3.forward);
            var points = new[] { new Vector3(0.5f, 0f, 2f), new Vector3(0.05f, 0f, 3f), new Vector3(0f, 0.2f, 2f) };
            Assert.AreEqual(1, ScanGeometry.NearestToLine(line, points, 7f));
        }

        [Test]
        public void No_point_within_the_angle_is_minus_one()
        {
            var line = new Ray(Vector3.zero, Vector3.forward);
            Assert.AreEqual(-1, ScanGeometry.NearestToLine(line, new[] { new Vector3(1f, 0f, 2f), Vector3.zero }, 7f));
            Assert.AreEqual(-1, ScanGeometry.NearestToLine(line, new Vector3[0], 7f));
        }

        [Test]
        public void An_image_box_becomes_a_flat_quad_at_its_objects_distance()
        {
            Ray From(float x, float y) => new Ray(Vector3.zero, new Vector3(x, y, 1f).normalized);
            var quad = ScanGeometry.QuadOnPlane(From(-0.1f, -0.1f), From(0.1f, -0.1f), From(0.1f, 0.1f), From(-0.1f, 0.1f),
                new Vector3(0f, 0f, 2f), Vector3.forward);
            Assert.IsTrue(quad.HasValue);
            Assert.That(Vector3.Distance(quad.Value.BottomLeft, new Vector3(-0.2f, -0.2f, 2f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(quad.Value.TopRight, new Vector3(0.2f, 0.2f, 2f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void A_plane_behind_the_sight_lines_gives_no_quad()
        {
            Ray From(float x, float y) => new Ray(Vector3.zero, new Vector3(x, y, 1f).normalized);
            Assert.IsNull(ScanGeometry.QuadOnPlane(From(-0.1f, -0.1f), From(0.1f, -0.1f), From(0.1f, 0.1f), From(-0.1f, 0.1f),
                new Vector3(0f, 0f, -2f), Vector3.forward));
        }

        static WorldQuad Square(Vector3 centre, float half) => new WorldQuad(
            centre + new Vector3(-half, -half, 0f), centre + new Vector3(half, -half, 0f),
            centre + new Vector3(half, half, 0f), centre + new Vector3(-half, half, 0f));

        [Test]
        public void The_box_around_a_quad_straight_ahead_spans_it_and_hangs_at_the_given_depth()
        {
            var box = ScanGeometry.BoxAround(Vector3.zero, Square(new Vector3(0f, 0f, 2f), 0.2f), 0.45f);
            Assert.IsTrue(box.HasValue);
            Assert.AreEqual(0.45f, box.Value.Depth);
            Assert.AreEqual(-0.1f, box.Value.Tangent.xMin, 1e-4f);
            Assert.AreEqual(0.1f, box.Value.Tangent.yMax, 1e-4f);
            Assert.That(Vector3.Distance(box.Value.Center, new Vector3(0f, 0f, 0.45f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void The_box_faces_a_quad_off_to_the_side_squarely()
        {
            var centre = new Vector3(2f, 0f, 2f);
            var box = ScanGeometry.BoxAround(Vector3.zero, Square(centre, 0.2f), 0.45f).Value;
            Assert.That(Vector3.Angle(box.Eye.rotation * Vector3.forward, centre), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(box.Center, centre), Is.LessThan(2f));
            Assert.AreEqual(0.45f, Vector3.Dot(box.Center, centre.normalized), 1e-3f);
        }

        [Test]
        public void A_quad_around_the_eye_has_no_box()
        {
            Assert.IsNull(ScanGeometry.BoxAround(Vector3.zero, Square(new Vector3(0f, 0f, 0.02f), 0.2f), 0.45f));
            Assert.IsNull(ScanGeometry.BoxAround(Vector3.zero, Square(Vector3.zero, 0.2f), 0.45f));
        }
    }
}
