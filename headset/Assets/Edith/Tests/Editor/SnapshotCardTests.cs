using System.Collections.Generic;
using System.Linq;
using Edith.Grab;
using Edith.Look;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using static Edith.Tests.SnapshotShelfTests;

namespace Edith.Tests
{
    public class SnapshotCardTests
    {
        GameObject parent;
        SnapshotRoom room;

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Room");
            room = new SnapshotRoom(parent.transform, null, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(parent);

        static string Shown(Snapshot snapshot, string label) =>
            snapshot.GetComponentsInChildren<Text>().Single(text => text.name == label).text;

        static Color[] BorderColors(Snapshot snapshot) =>
            snapshot.GetComponentsInChildren<Image>().Where(image => image.name == "Border").Select(image => image.color).ToArray();

        [Test]
        public void A_new_snapshot_says_it_is_looking()
        {
            var snapshot = room.Place(Box(0f));
            Assert.AreEqual("Looking…", Shown(snapshot, "Fact"));
            Assert.AreEqual(LookPhase.Looking, snapshot.Card.Phase);
        }

        [Test]
        public void The_card_says_looking_until_the_fact_then_shows_only_the_fact()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Named("Red m", pending: true));
            Assert.AreEqual("Looking…", Shown(snapshot, "Fact"));
            snapshot.Apply(LookEvent.Named("Red mug"));
            Assert.AreEqual("Looking…", Shown(snapshot, "Fact"));
            snapshot.Apply(LookEvent.Fact("Mugs are old."));
            snapshot.Card.Advance(10f);
            snapshot.Apply(LookEvent.Done());
            Assert.AreEqual("Mugs are old.", Shown(snapshot, "Fact"));
            Assert.AreEqual(1, snapshot.GetComponentsInChildren<Text>().Length, "one line of text, no name above it");
        }

        static float WorldWidth(Component part)
        {
            var rect = (RectTransform)part.transform;
            return rect.rect.width * rect.lossyScale.x;
        }

        [Test]
        public void The_fact_wraps_within_the_photo_width()
        {
            var snapshot = room.Place(Box(0f));
            var photo = snapshot.GetComponentsInChildren<RawImage>().Single(image => image.name == "Photo");
            var fact = snapshot.GetComponentsInChildren<Text>().Single(text => text.name == "Fact");
            Assert.AreEqual(WorldWidth(photo), WorldWidth(fact), 1e-4f);
        }

        [Test]
        public void A_look_without_a_fact_shows_its_name()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Named("Red mug"));
            snapshot.Apply(LookEvent.Done());
            snapshot.Card.Advance(10f);
            snapshot.Apply(LookEvent.Done());
            Assert.AreEqual("Red mug", Shown(snapshot, "Fact"));
        }

        [Test]
        public void A_failed_look_shows_its_message()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Failure("camera", "The camera is not ready."));
            Assert.AreEqual("The camera is not ready.", Shown(snapshot, "Fact"));
        }

        [Test]
        public void The_fact_takes_the_rarity_colour()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Fact("Mugs are old."));
            snapshot.Apply(LookEvent.Rated(Rarity.Epic));
            Assert.AreEqual(RarityColors.For(Rarity.Epic), snapshot.GetComponentsInChildren<Text>().Single().color);
        }

        [Test]
        public void The_border_takes_the_rarity_colour()
        {
            var snapshot = room.Place(Box(0f));
            var waiting = BorderColors(snapshot);
            Assert.AreEqual(4, waiting.Length);
            snapshot.Apply(LookEvent.Rated(Rarity.Legendary));
            Assert.That(BorderColors(snapshot), Has.All.EqualTo(RarityColors.For(Rarity.Legendary)));
            Assert.AreNotEqual(waiting[0], BorderColors(snapshot)[0]);
        }

        [Test]
        public void Every_snapshot_claims_a_fist_but_only_a_finished_look_moves()
        {
            var host = new GameObject("Grab");
            host.SetActive(false);
            try
            {
                var grab = host.AddComponent<SnapshotGrab>();
                grab.Bind(room);
                var writing = room.Place(Box(-0.3f));
                var written = room.Place(Box(0.3f));
                written.Apply(LookEvent.Done());
                var failed = room.Place(Box(0.9f));
                failed.Apply(LookEvent.Failure("network", "EDITH cannot be reached."));

                var targets = new List<(int id, SightBox box)>();
                grab.AddGrabTargets(targets);
                CollectionAssert.AreEquivalent(new[] { writing.Id, written.Id, failed.Id }, targets.Select(target => target.id));
                Assert.IsTrue(SnapshotGrab.CanCarry(written));
                Assert.IsFalse(SnapshotGrab.CanCarry(writing), "still being written");
                Assert.IsFalse(SnapshotGrab.CanCarry(failed), "a failed look never moves");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
