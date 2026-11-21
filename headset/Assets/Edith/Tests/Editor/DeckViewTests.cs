using System;
using System.Collections.Generic;
using System.Linq;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Edith.Tests
{
    public class DeckViewTests
    {
        GameObject parent;
        Font font;
        DeckView view;
        List<Texture2D> textures;

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Deck");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view = new DeckView(parent.transform, font, null);
            textures = new List<Texture2D>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(parent);
            foreach (var texture in textures) Object.DestroyImmediate(texture);
        }

        static List<Memory> Memories(int count) => Enumerable.Range(0, count)
            .Select(i => new Memory(100 + i, $"Thing {i}", $"Fact {i}.", Rarity.Rare, "Home", new DateTime(2026, 11, 21).AddDays(-i)))
            .ToList();

        static Texture NoPhoto(int id) => null;

        Texture2D Photo(int width, int height)
        {
            var texture = new Texture2D(width, height);
            textures.Add(texture);
            return texture;
        }

        GameObject[] Cards => parent.transform.Cast<Transform>().Where(card => card.name == "Card").Select(card => card.gameObject).ToArray();

        GameObject[] Drawn => Cards.Where(card => card.activeSelf).ToArray();

        /// The card nearest the middle of the deck.
        GameObject Middle => Drawn.OrderBy(card => Mathf.Abs(card.transform.localPosition.x)).First();

        static string Shown(GameObject card, string label) =>
            card.GetComponentsInChildren<Text>(true).Single(text => text.name == label).text;

        static RectTransform Part(GameObject card, string name) =>
            card.GetComponentsInChildren<RectTransform>(true).Single(part => part.name == name);

        [Test]
        public void The_middle_card_shows_the_fact_and_the_caption_without_the_name()
        {
            view.Show(Memories(5), 0f, NoPhoto);
            Assert.AreEqual("", Shown(Middle, "Name"));
            Assert.AreEqual("Fact 0.", Shown(Middle, "Fact"));
            Assert.AreEqual("Home · 11/21", Shown(Middle, "Caption"));
        }

        [Test]
        public void The_frame_takes_the_colour_of_the_rarity()
        {
            view.Show(Memories(1), 0f, NoPhoto);
            Assert.AreEqual(RarityColors.For(Rarity.Rare), Part(Middle, "Frame").GetComponent<Image>().color);
        }

        [Test]
        public void A_memory_without_a_fact_shows_its_name_with_the_caption_right_under_it()
        {
            var plain = new List<Memory> { new Memory(1, "Fern", null, null, null, new DateTime(2026, 3, 4)) };
            view.Show(plain, 0f, NoPhoto);
            Assert.AreEqual("Fern", Shown(Middle, "Name"));
            Assert.AreEqual("", Shown(Middle, "Fact"));
            Assert.AreEqual("3/4", Shown(Middle, "Caption"));
            Assert.AreEqual(Part(Middle, "Fact").anchoredPosition.y, Part(Middle, "Caption").anchoredPosition.y);

            view.Show(Memories(1), 0f, NoPhoto);
            Assert.That(Part(Middle, "Caption").anchoredPosition.y, Is.LessThan(Part(Middle, "Fact").anchoredPosition.y));
        }

        [Test]
        public void At_the_newest_card_only_older_cards_stand_beside_it()
        {
            view.Show(Memories(10), 0f, NoPhoto);
            Assert.AreEqual(3, Drawn.Length, "the middle card and two older ones");
            Assert.IsTrue(Drawn.All(card => card.transform.localPosition.x >= 0f));
        }

        [Test]
        public void In_the_middle_of_the_deck_two_cards_show_on_each_side()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            Assert.AreEqual(5, Drawn.Length);
            Assert.AreEqual("Fact 5.", Shown(Middle, "Fact"));
            CollectionAssert.AreEquivalent(new[] { "Fact 3.", "Fact 4.", "Fact 5.", "Fact 6.", "Fact 7." }, Drawn.Select(card => Shown(card, "Fact")));
        }

        [Test]
        public void The_cards_never_grow_with_the_number_of_memories()
        {
            var memories = Memories(60);
            for (var position = 0f; position <= 59f; position += 0.37f) view.Show(memories, position, NoPhoto);
            Assert.AreEqual(2 * DeckLayout.Reach + 1, Cards.Length);
            Assert.AreEqual("Fact 59.", Shown(Middle, "Fact"));
        }

        [Test]
        public void Between_two_cards_both_are_drawn_and_neither_is_square_on()
        {
            view.Show(Memories(10), 4.5f, NoPhoto);
            var facts = Drawn.Select(card => Shown(card, "Fact")).ToArray();
            CollectionAssert.Contains(facts, "Fact 4.");
            CollectionAssert.Contains(facts, "Fact 5.");
            Assert.IsTrue(Drawn.All(card => Mathf.Abs(card.transform.localPosition.x) > 0.01f));
        }

        [Test]
        public void A_photo_keeps_its_own_shape_and_its_border_hugs_it()
        {
            var wide = Photo(400, 100);
            view.Show(Memories(1), 0f, _ => wide);
            var frame = Part(Middle, "Frame").sizeDelta;
            var photo = Part(Middle, "Photo").sizeDelta;
            Assert.AreEqual(4f, photo.x / photo.y, 1e-3f);
            Assert.AreEqual(frame.x - photo.x, frame.y - photo.y, 1e-3f, "an even border, no bars beside the photo");

            var tall = Photo(100, 400);
            view.Show(Memories(1), 0f, _ => tall);
            var tallFrame = Part(Middle, "Frame").sizeDelta;
            photo = Part(Middle, "Photo").sizeDelta;
            Assert.AreEqual(0.25f, photo.x / photo.y, 1e-3f);
            Assert.AreEqual(tallFrame.x - photo.x, tallFrame.y - photo.y, 1e-3f);
        }

        [Test]
        public void The_text_stays_put_whatever_the_photo_shape()
        {
            var wide = Photo(400, 100);
            view.Show(Memories(1), 0f, _ => wide);
            var caption = Part(Middle, "Caption").position;
            var wideBottom = Part(Middle, "Frame").anchoredPosition.y - Part(Middle, "Frame").sizeDelta.y * 0.5f;
            var tall = Photo(100, 400);
            view.Show(Memories(1), 0f, _ => tall);
            Assert.AreEqual(caption, Part(Middle, "Caption").position);
            Assert.AreEqual(wideBottom, Part(Middle, "Frame").anchoredPosition.y - Part(Middle, "Frame").sizeDelta.y * 0.5f, 1e-3f);
        }

        [Test]
        public void A_card_shows_no_empty_frame_while_its_photo_is_missing()
        {
            view.Show(Memories(1), 0f, NoPhoto);
            Assert.IsFalse(Part(Middle, "Frame").gameObject.activeSelf);
            var photo = Photo(8, 8);
            view.Show(Memories(1), 0f, _ => photo);
            Assert.IsTrue(Part(Middle, "Frame").gameObject.activeSelf);
        }

        [Test]
        public void A_card_shows_its_photo_once_it_arrives()
        {
            var memories = Memories(3);
            view.Show(memories, 0f, NoPhoto);
            Assert.IsNull(Part(Middle, "Photo").GetComponent<RawImage>().texture);
            var photo = Photo(8, 8);
            view.Show(memories, 0f, id => id == 100 ? photo : null);
            Assert.AreSame(photo, Part(Middle, "Photo").GetComponent<RawImage>().texture);
        }

        [Test]
        public void A_pinch_in_line_with_a_card_finds_it()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            var eye = parent.transform.position;
            Assert.AreEqual(5, view.Hit(eye, Part(Middle, "Frame").position * 0.6f));
            var right = Drawn.Single(card => Shown(card, "Fact") == "Fact 6.");
            var outer = Part(right, "Frame");
            Assert.AreEqual(6, view.Hit(eye, (outer.position + outer.right * 0.03f) * 0.6f));
            var left = Drawn.Single(card => Shown(card, "Fact") == "Fact 3.");
            var far = Part(left, "Frame");
            Assert.AreEqual(3, view.Hit(eye, (far.position - far.right * 0.05f) * 0.6f));
        }

        [Test]
        public void A_pinch_off_every_card_finds_none()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            Assert.AreEqual(-1, view.Hit(Vector3.zero, new Vector3(0f, 0.3f, 0.4f)));
            Assert.AreEqual(-1, view.Hit(Vector3.zero, new Vector3(0.5f, -0.03f, 0.3f)));
        }

        [Test]
        public void Where_cards_overlap_the_one_nearer_the_middle_is_found()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            var first = Part(Drawn.Single(card => Shown(card, "Fact") == "Fact 6."), "Frame");
            // The inner half of the second card on a side lies behind the first.
            var second = Part(Drawn.Single(card => Shown(card, "Fact") == "Fact 7."), "Frame");
            Assert.AreEqual(6, view.Hit(Vector3.zero, (second.position - second.right * 0.02f) * 0.6f));
            Assert.AreEqual(6, view.Hit(Vector3.zero, first.position * 0.6f));
        }

        [Test]
        public void A_notice_is_one_card_with_its_message_and_no_pinch_finds_it()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            view.ShowNotice("Nothing collected yet", "Grab a snapshot.");
            Assert.AreEqual(1, Drawn.Length);
            Assert.AreEqual("Nothing collected yet", Shown(Middle, "Name"));
            Assert.AreEqual("Grab a snapshot.", Shown(Middle, "Fact"));
            Assert.AreEqual(-1, view.Hit(Vector3.zero, Part(Middle, "Frame").position * 0.6f));
        }

        [Test]
        public void The_deck_draws_again_after_a_notice()
        {
            var memories = Memories(4);
            view.Show(memories, 1f, NoPhoto);
            view.ShowNotice("Opening", null);
            view.Show(memories, 1f, NoPhoto);
            Assert.AreEqual("Fact 1.", Shown(Middle, "Fact"));
        }

        [Test]
        public void A_faded_out_deck_draws_nothing()
        {
            view.Show(Memories(10), 5f, NoPhoto, 0f);
            Assert.AreEqual(0, Drawn.Length);
        }
    }
}
