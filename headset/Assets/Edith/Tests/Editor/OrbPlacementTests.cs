using System.Collections.Generic;
using Edith.Ask;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;
using static Edith.Tests.SnapshotShelfTests;

namespace Edith.Tests
{
    public class OrbPlacementTests
    {
        static readonly float HomeRow = AnswerOrb.OrbMetres + AnswerOrb.GapMetres + AnswerOrb.HomeCaptionMetres;

        GameObject eye, parent;
        SnapshotRoom room;
        readonly List<AnswerOrb> orbs = new List<AnswerOrb>();

        Pose EyePose => new Pose(eye.transform.position, eye.transform.rotation);

        [SetUp]
        public void SetUp()
        {
            eye = new GameObject("Eye");
            parent = new GameObject("Room");
            room = new SnapshotRoom(parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var orb in orbs) Object.DestroyImmediate(orb.gameObject);
            orbs.Clear();
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(eye);
        }

        AnswerOrb Create(Snapshot asked = null)
        {
            var orb = AnswerOrb.Create(eye.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null, room, asked);
            orbs.Add(orb);
            return orb;
        }

        Rect SeenRow(AnswerOrb orb, float rowMetres) =>
            OrbPlacement.WorldRow(EyePose, orb.transform.position, orb.transform.rotation, rowMetres).Value;

        Rect SeenSnapshot(Snapshot snapshot) =>
            OrbPlacement.Covered(EyePose, new Pose(snapshot.transform.position, snapshot.transform.rotation), snapshot.Box.Size).Value;

        [Test]
        public void With_nothing_in_the_way_it_appears_at_home_low_and_left_without_gliding_in()
        {
            eye.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 40f, 0f));
            var orb = Create();
            Assert.That(Vector3.Distance(orb.transform.position, OrbPlacement.AtHead(EyePose, OrbPlacement.Home)), Is.LessThan(1e-5f));
            var local = eye.transform.InverseTransformPoint(orb.transform.position);
            Assert.That(local.x, Is.LessThan(-0.15f), "well left of the line of sight");
            Assert.That(local.y, Is.LessThan(-0.1f), "below the line of sight");
            Assert.IsFalse(orb.Perched);
            var towardOrb = (orb.transform.position - eye.transform.position).normalized;
            Assert.That(Vector3.Dot(orb.transform.forward, towardOrb), Is.GreaterThan(0.9999f), "faces the eye from the corner");
            Assert.That(Vector3.Dot(orb.transform.right, eye.transform.up), Is.EqualTo(0f).Within(1e-4f), "level with the head");
        }

        [Test]
        public void A_snapshot_over_home_makes_it_appear_clear_of_it()
        {
            var snapshot = room.Place(Box(-0.06f, -0.12f));
            Assert.IsTrue(OrbPlacement.HeadRow(OrbPlacement.Home, HomeRow).Overlaps(SeenSnapshot(snapshot)), "home is covered");
            var orb = Create();
            Assert.IsFalse(SeenRow(orb, HomeRow).Overlaps(SeenSnapshot(snapshot)));
        }

        [Test]
        public void It_stays_clear_of_every_snapshot_in_view()
        {
            var snapshots = new[] { room.Place(Box(-0.06f, -0.12f)), room.Place(Box(0.2f, 0.1f)), room.Place(Box(-0.3f, 0.12f)) };
            var orb = Create();
            var row = SeenRow(orb, HomeRow);
            foreach (var snapshot in snapshots)
                Assert.IsFalse(row.Overlaps(SeenSnapshot(snapshot)), $"clear of snapshot {snapshot.Id}");
        }

        [Test]
        public void Asked_about_a_snapshot_it_appears_perched_above_its_photo()
        {
            var asked = room.Place(Box(0f));
            var orb = Create(asked);
            Assert.IsTrue(orb.Perched);
            var photo = new Pose(asked.transform.position, asked.transform.rotation);
            Assert.That(Vector3.Distance(orb.transform.position, OrbPlacement.Perch(photo, asked.Box.Size)), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(orb.transform.rotation, asked.transform.rotation), Is.LessThan(0.01f), "in the photo's plane");
        }

        [Test]
        public void A_perch_that_would_cover_another_snapshot_gives_way_to_a_clear_spot()
        {
            var asked = room.Place(Box(0f));
            var above = room.Place(Box(0f, 0.2f));
            var orb = Create(asked);
            Assert.IsFalse(orb.Perched);
            var row = SeenRow(orb, HomeRow);
            Assert.IsFalse(row.Overlaps(SeenSnapshot(asked)));
            Assert.IsFalse(row.Overlaps(SeenSnapshot(above)));
        }

        [Test]
        public void A_perched_orb_sits_above_the_photo_with_its_left_edge_on_the_photos()
        {
            var photo = new Pose(new Vector3(0.3f, 1.4f, 0.5f), Quaternion.Euler(10f, 25f, 5f));
            var size = new Vector2(0.2f, 0.15f);
            var local = Quaternion.Inverse(photo.rotation) * (OrbPlacement.Perch(photo, size) - photo.position);
            Assert.That(local.x - AnswerOrb.OrbMetres * 0.5f, Is.EqualTo(-size.x * 0.5f).Within(1e-5f), "left edges line up");
            Assert.That(local.y - AnswerOrb.OrbMetres * 0.5f, Is.EqualTo(size.y * 0.5f + OrbPlacement.PerchGapMetres).Within(1e-5f),
                "a small gap above the top edge");
            Assert.That(local.z, Is.EqualTo(0f).Within(1e-5f), "in the photo's plane");
        }

        [Test]
        public void A_snapshot_takes_the_view_of_its_card_text_under_the_photo_too()
        {
            var photo = new Pose(new Vector3(0f, 0f, 0.4f), Quaternion.identity);
            var seen = OrbPlacement.Covered(new Pose(Vector3.zero, Quaternion.identity), photo, new Vector2(0.2f, 0.15f)).Value;
            Assert.That(seen.yMax, Is.EqualTo(0.078f / 0.4f).Within(1e-4f), "the top border");
            Assert.That(seen.yMin, Is.LessThan(-0.075f / 0.4f - 0.1f), "well below the photo");
        }

        [Test]
        public void The_nearest_clear_spot_is_clear_and_close()
        {
            var covered = new List<Rect> { OrbPlacement.HeadRow(OrbPlacement.Home, HomeRow) };
            var spot = OrbPlacement.Nearest(OrbPlacement.Home, covered, HomeRow);
            Assert.AreEqual(0f, OrbPlacement.Overlap(OrbPlacement.HeadRow(spot, HomeRow), covered));
            Assert.That(Vector2.Distance(spot, OrbPlacement.Home), Is.LessThan(0.25f));
        }

        [Test]
        public void A_snapshot_behind_the_eye_takes_none_of_the_view()
        {
            var behind = new Pose(new Vector3(0f, 0f, -0.4f), Quaternion.identity);
            Assert.IsNull(OrbPlacement.Covered(new Pose(Vector3.zero, Quaternion.identity), behind, new Vector2(0.2f, 0.15f)));
        }
    }
}
