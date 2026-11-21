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
            Assert.AreEqual("Looking…", Shown(snapshot, "Name"));
            Assert.AreEqual(LookPhase.Looking, snapshot.Card.Phase);
        }

        [Test]
        public void Events_write_the_name_and_the_fact_under_the_photo()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Named("Red m", pending: true));
            Assert.AreEqual("Red m", Shown(snapshot, "Name"));
            snapshot.Apply(LookEvent.Named("Red mug"));
            snapshot.Apply(LookEvent.Fact("Mugs are old."));
            snapshot.Card.Advance(10f);
            snapshot.Apply(LookEvent.Done());
            Assert.AreEqual("Red mug", Shown(snapshot, "Name"));
            Assert.AreEqual("Mugs are old.", Shown(snapshot, "Fact"));
        }

        [Test]
        public void A_failed_look_shows_its_message_in_place_of_a_fact()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Failure("camera", "The camera is not ready."));
            Assert.AreEqual("The camera is not ready.", Shown(snapshot, "Fact"));
            Assert.AreEqual("", Shown(snapshot, "Name"));
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
        public void A_snapshot_whose_card_has_not_finished_is_not_a_grab_target()
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
                CollectionAssert.AreEquivalent(new[] { written.Id, failed.Id }, targets.Select(target => target.id));
                CollectionAssert.DoesNotContain(targets.Select(target => target.id), writing.Id);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
