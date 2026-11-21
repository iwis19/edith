using System.Collections.Generic;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class SnapshotShelfTests
    {
        /// A 20 cm by 15 cm box centred on (x, y) at `depth`, seen from an eye at `eye` turned by `yaw`.
        internal static SightBox Box(float x, float y = 0f, float yaw = 0f, float depth = 0.4f, Vector3 eye = default) =>
            new SightBox(new Pose(eye, Quaternion.Euler(0f, yaw, 0f)),
                Rect.MinMaxRect((x - 0.10f) / depth, (y - 0.075f) / depth, (x + 0.10f) / depth, (y + 0.075f) / depth), depth);

        [Test]
        public void Holds_four_and_the_fifth_removes_the_oldest()
        {
            var shelf = new SnapshotShelf<string>();
            foreach (var (name, x) in new[] { ("a", -0.9f), ("b", -0.3f), ("c", 0.3f), ("d", 0.9f) })
                Assert.IsEmpty(shelf.Add(name, Box(x)));
            CollectionAssert.AreEqual(new[] { "a" }, shelf.Add("e", Box(0f, y: 0.5f)));
            Assert.AreEqual(4, shelf.Count);
        }

        [Test]
        public void Reframing_the_same_spot_replaces_the_old_snapshot()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            CollectionAssert.AreEqual(new[] { "old" }, shelf.Add("new", Box(0.05f, 0.02f, yaw: 5f)));
            Assert.AreEqual(1, shelf.Count);
        }

        [Test]
        public void Covered_snapshots_leave_before_the_oldest_is_evicted()
        {
            var shelf = new SnapshotShelf<string>();
            foreach (var (name, x) in new[] { ("a", -0.9f), ("b", -0.3f), ("c", 0.3f), ("d", 0.9f) })
                shelf.Add(name, Box(x));
            CollectionAssert.AreEqual(new[] { "c" }, shelf.Add("e", Box(0.3f)), "the covered one makes room, so a stays");
        }

        [Test]
        public void A_box_over_two_snapshots_erases_both()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("left", Box(-0.3f));
            shelf.Add("middle", Box(0f));
            shelf.Add("right", Box(0.3f));
            CollectionAssert.AreEquivalent(new[] { "middle", "right" }, shelf.Add("new", Box(0.15f)));
        }

        [Test]
        public void Side_by_side_snapshots_both_stay()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("left", Box(-0.15f));
            Assert.IsEmpty(shelf.Add("right", Box(0.15f)));
        }

        [Test]
        public void Barely_touching_edges_do_not_count()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            Assert.IsEmpty(shelf.Add("new", Box(0.18f)));
        }

        [Test]
        public void A_nearer_box_drawn_over_a_farther_snapshot_erases_it()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("far", Box(0f, depth: 0.6f));
            CollectionAssert.AreEqual(new[] { "far" }, shelf.Add("near", Box(0f, depth: 0.3f)));
        }

        [Test]
        public void A_box_drawn_from_across_the_room_covers_what_it_lines_up_with()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            var fromBehind = new SightBox(new Pose(new Vector3(0f, 0f, -2f), Quaternion.identity),
                Rect.MinMaxRect(-0.05f, -0.04f, 0.05f, 0.04f), 0.4f);
            CollectionAssert.AreEqual(new[] { "old" }, shelf.Add("new", fromBehind));
        }

        [Test]
        public void A_box_facing_away_or_behind_leaves_the_snapshot()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            Assert.IsEmpty(shelf.Add("turned", Box(0f, yaw: 45f)));
            Assert.IsEmpty(shelf.Add("behind", Box(0f, yaw: 180f)));
            Assert.AreEqual(3, shelf.Count);
        }

        [Test]
        public void Under_lists_without_removing_and_clears_the_list_first()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            var under = new List<string> { "stale" };
            shelf.Under(Box(0.02f), under);
            CollectionAssert.AreEqual(new[] { "old" }, under);
            Assert.AreEqual(1, shelf.Count);
        }

        [Test]
        public void Remove_takes_a_snapshot_out()
        {
            var shelf = new SnapshotShelf<string>();
            shelf.Add("old", Box(0f));
            Assert.IsTrue(shelf.Remove("old"));
            Assert.IsFalse(shelf.Remove("old"));
            Assert.AreEqual(0, shelf.Count);
        }
    }
}
