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
            snapshot.GetComponentsInChildren<Image>().Where(image => image.name == "Border" && image.transform.parent.name == "Glass").Select(image => image.color).ToArray();

        [Test]
        public void A_new_snapshot_says_it_is_looking()
        {
            var snapshot = room.Place(Box(0f));
            Assert.AreEqual("Looking…", Shown(snapshot, "Fact"));
            Assert.AreEqual(LookPhase.Looking, snapshot.Card.Phase);
        }

        [Test]
        public void The_card_says_looking_until_the_fact_and_never_shows_the_name()
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
            Assert.AreEqual(1, snapshot.GetComponentsInChildren<Text>().Length, "the fact alone");
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

        static RectTransform GlassBody(Snapshot snapshot) =>
            snapshot.GetComponentsInChildren<Image>().Single(image => image.name == "Body" && image.transform.parent.name == "Glass").rectTransform;

        [Test]
        public void A_new_card_opens_down_out_of_its_photo()
        {
            var snapshot = room.Place(Box(0f));
            var photo = snapshot.GetComponentsInChildren<RawImage>().Single(image => image.name == "Photo").rectTransform;
            var text = snapshot.GetComponentsInChildren<CanvasGroup>().Single(group => group.name == "Text");
            var closed = GlassBody(snapshot).rect.size;
            var closedTop = GlassBody(snapshot).parent.localPosition.y + closed.y * 0.5f;
            Assert.AreEqual(closed.x - photo.rect.width, closed.y - photo.rect.height, 1e-3f, "hugs the photo, no room for text yet");
            Assert.AreEqual(0f, text.alpha);

            snapshot.Step(1f);
            var open = GlassBody(snapshot).rect.size;
            Assert.Greater(open.y, closed.y + 1f, "grown down to hold the text");
            Assert.AreEqual(closed.x, open.x, 1e-3f, "same width");
            Assert.AreEqual(closedTop, GlassBody(snapshot).parent.localPosition.y + open.y * 0.5f, 1e-3f, "top stays put");
            Assert.AreEqual(1f, text.alpha);
        }

        [Test]
        public void A_failed_look_shows_its_message()
        {
            var snapshot = room.Place(Box(0f));
            snapshot.Apply(LookEvent.Failure("camera", "The camera is not ready."));
            Assert.AreEqual("The camera is not ready.", Shown(snapshot, "Fact"));
        }

        [Test]
        public void The_fact_is_plain_white_with_no_outline_whatever_the_rarity()
        {
            var snapshot = room.Place(Box(0f));
            var fact = snapshot.GetComponentsInChildren<Text>().Single(text => text.name == "Fact");
            Assert.AreEqual(Color.white, fact.color, "white while looking");
            snapshot.Apply(LookEvent.Fact("Mugs are old."));
            snapshot.Apply(LookEvent.Rated(Rarity.Epic));
            Assert.AreEqual(Color.white, fact.color);
            Assert.IsEmpty(fact.GetComponents<Shadow>(), "no outline or shadow around the letters");
        }

        [Test]
        public void The_glass_outline_takes_the_rarity_colour()
        {
            var snapshot = room.Place(Box(0f));
            var waiting = BorderColors(snapshot);
            Assert.AreEqual(1, waiting.Length, "one outline around the glass");
            snapshot.Apply(LookEvent.Rated(Rarity.Legendary));
            Assert.That(BorderColors(snapshot), Has.All.EqualTo(RarityColors.For(Rarity.Legendary)));
            Assert.AreNotEqual(waiting[0], BorderColors(snapshot)[0]);
        }

        [Test]
        public void A_rated_snapshot_draws_its_outline_bold()
        {
            var snapshot = room.Place(Box(0f));
            Image Outline() => snapshot.GetComponentsInChildren<Image>().Single(image => image.name == "Border" && image.transform.parent.name == "Glass");
            Assert.AreEqual(Edith.UI.Theme.Edge, Outline().sprite, "thin while waiting for its rarity");
            snapshot.Apply(LookEvent.Rated(Rarity.Rare));
            Assert.AreEqual(Edith.UI.Theme.BoldEdge, Outline().sprite);
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
