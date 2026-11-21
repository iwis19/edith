using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;
using static Edith.Tests.SnapshotShelfTests;

namespace Edith.Tests
{
    public class SnapshotRoomTests
    {
        GameObject parent;
        SnapshotRoom room;

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Room");
            room = new SnapshotRoom(parent.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(parent);

        [Test]
        public void A_snapshot_hangs_in_its_box_facing_the_eye()
        {
            var box = Box(0.1f, 0.05f, yaw: 30f);
            var snapshot = room.Place(box);
            Assert.That(Vector3.Distance(snapshot.transform.position, box.Center), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(snapshot.transform.rotation, box.Eye.rotation), Is.LessThan(0.01f));
            var corners = new Vector3[4];
            ((RectTransform)snapshot.transform).GetWorldCorners(corners);
            // Bottom left, top left, top right, bottom right.
            Assert.AreEqual(0.20f, Vector3.Distance(corners[0], corners[3]), 1e-4f, "width");
            Assert.AreEqual(0.15f, Vector3.Distance(corners[0], corners[1]), 1e-4f, "height");
        }

        [Test]
        public void Dragging_over_a_snapshot_fades_it_and_dragging_off_brings_it_back()
        {
            var old = room.Place(Box(0f));
            room.Preview(Box(0.02f));
            Assert.IsFalse(old.Visible);
            room.Preview(Box(0.5f));
            Assert.IsTrue(old.Visible);
            room.Preview(Box(0.02f));
            room.CancelPreview();
            Assert.IsTrue(old.Visible);
            Assert.IsFalse(old.Leaving);
        }

        [Test]
        public void Releasing_over_a_snapshot_makes_it_leave()
        {
            var old = room.Place(Box(0f));
            room.Preview(Box(0.02f));
            var placed = room.Place(Box(0.02f));
            Assert.IsTrue(old.Leaving);
            Assert.IsTrue(placed.Visible);
            Assert.AreEqual(1, room.Snapshots.Count);
        }

        [Test]
        public void Releasing_somewhere_else_brings_back_what_the_drag_covered()
        {
            var old = room.Place(Box(0f));
            room.Preview(Box(0.02f));
            room.Place(Box(0.5f));
            Assert.IsTrue(old.Visible);
            Assert.IsFalse(old.Leaving);
            Assert.AreEqual(2, room.Snapshots.Count);
        }

        [Test]
        public void The_fifth_snapshot_makes_the_oldest_leave()
        {
            var oldest = room.Place(Box(-0.9f));
            room.Place(Box(-0.3f));
            room.Place(Box(0.3f));
            room.Place(Box(0.9f));
            room.Place(Box(0f, y: 0.5f));
            Assert.IsTrue(oldest.Leaving);
            Assert.AreEqual(4, room.Snapshots.Count);
        }

        [Test]
        public void Dismiss_makes_a_snapshot_leave_once()
        {
            var snapshot = room.Place(Box(0f));
            Assert.IsTrue(room.Dismiss(snapshot));
            Assert.IsTrue(snapshot.Leaving);
            Assert.IsFalse(room.Dismiss(snapshot));
            Assert.AreEqual(0, room.Snapshots.Count);
        }

        [Test]
        public void Pocket_takes_a_snapshot_off_the_shelf_and_announces_it_once()
        {
            var kept = room.Place(Box(-0.5f));
            var snapshot = room.Place(Box(0.5f));
            var pocketed = new System.Collections.Generic.List<Snapshot>();
            room.Pocketed += pocketed.Add;

            Assert.IsTrue(room.Pocket(snapshot, Vector3.down));
            Assert.IsFalse(room.Pocket(snapshot, Vector3.down));

            CollectionAssert.AreEqual(new[] { snapshot }, pocketed);
            Assert.IsTrue(snapshot.Leaving);
            Assert.AreEqual(1, room.Snapshots.Count);
            Assert.AreSame(kept, room.Snapshots[0].item);
            Assert.IsFalse(room.Dismiss(snapshot));
        }

        static void Settle(Snapshot snapshot, int frames = 400)
        {
            var update = typeof(Snapshot).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (var i = 0; i < frames; i++) update.Invoke(snapshot, null);
        }

        [Test]
        public void A_carried_snapshot_follows_the_hold_and_floats_back_to_its_box_on_release()
        {
            var box = Box(0.1f);
            var snapshot = room.Place(box);
            var held = box.Center + new Vector3(0f, -0.3f, -0.2f);

            snapshot.Hold(held, Quaternion.Euler(0f, 40f, 0f));
            Settle(snapshot);
            Assert.That(Vector3.Distance(snapshot.transform.position, held), Is.LessThan(1e-3f), "carried");

            snapshot.FloatBack();
            Settle(snapshot);
            Assert.That(Vector3.Distance(snapshot.transform.position, box.Center), Is.LessThan(1e-3f), "back at its box");
            Assert.That(Quaternion.Angle(snapshot.transform.rotation, box.Eye.rotation), Is.LessThan(0.2f));
        }

        [Test]
        public void A_leaving_snapshot_ignores_the_hold()
        {
            var box = Box(0.1f);
            var snapshot = room.Place(box);
            room.Dismiss(snapshot);

            snapshot.Hold(box.Center + Vector3.down, Quaternion.identity);

            Assert.IsFalse(Carried(snapshot));
        }

        static bool Carried(Snapshot snapshot) =>
            (bool)typeof(Snapshot).GetField("carried", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(snapshot);

        [Test]
        public void Snapshots_have_distinct_ids()
        {
            Assert.AreNotEqual(room.Place(Box(-0.5f)).Id, room.Place(Box(0.5f)).Id);
        }
    }
}
