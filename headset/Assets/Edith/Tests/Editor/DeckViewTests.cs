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
        public void The_middle_card_shows_the_fact_in_the_rarity_colour()
        {
            view.Show(Memories(5), 0f, NoPhoto);
            Assert.AreEqual("Fact 0.", Shown(Middle, "Fact"));
            Assert.AreEqual(RarityColors.For(Rarity.Rare), Part(Middle, "Fact").GetComponent<Text>().color);
        }

        [Test]
        public void A_card_has_no_badge_place_name_or_day()
        {
            var photo = Photo(8, 8);
            view.Show(Memories(1), 0f, _ => photo);
            var names = Middle.GetComponentsInChildren<RectTransform>(true).Select(part => part.name).ToArray();
            foreach (var gone in new[] { "BadgeBox", "Badge", "Place", "Day", "Name" }) CollectionAssert.DoesNotContain(names, gone);
            var texts = Middle.GetComponentsInChildren<Text>(true).Select(text => text.text).Where(text => text != "").ToArray();
            CollectionAssert.AreEqual(new[] { "Fact 0." }, texts);
        }

        [Test]
        public void The_border_takes_the_colour_of_the_rarity()
        {
            view.Show(Memories(1), 0f, NoPhoto);
            var edges = Middle.GetComponentsInChildren<Image>(true).Where(edge => edge.name == "Border").ToArray();
            Assert.AreEqual(4, edges.Length);
            Assert.IsTrue(edges.All(edge => edge.color == RarityColors.For(Rarity.Rare)));
        }

        [Test]
        public void A_memory_without_a_rarity_has_a_pale_border_and_white_text()
        {
            var plain = new List<Memory> { new Memory(1, "Fern", "A fern.", null, null, new DateTime(2026, 3, 4)) };
            view.Show(plain, 0f, NoPhoto);
            var rare = RarityColors.For(Rarity.Rare);
            Assert.AreNotEqual(rare, Middle.GetComponentsInChildren<Image>(true).First(edge => edge.name == "Border").color);
            Assert.AreEqual(1f, Part(Middle, "Fact").GetComponent<Text>().color.r, 0.05f);
        }

        [Test]
        public void A_memory_without_a_fact_shows_its_name()
        {
            var plain = new List<Memory> { new Memory(1, "Fern", null, null, null, new DateTime(2026, 3, 4)) };
            view.Show(plain, 0f, NoPhoto);
            Assert.AreEqual("Fern", Shown(Middle, "Fact"));
        }

        [Test]
        public void A_photo_keeps_its_own_shape_and_the_border_hugs_it()
        {
            var wide = Photo(400, 100);
            view.Show(Memories(1), 0f, _ => wide);
            var photo = Part(Middle, "Photo").sizeDelta;
            Assert.AreEqual(4f, photo.x / photo.y, 1e-3f);
            AssertBorderHugs(photo);

            var tall = Photo(100, 400);
            view.Show(Memories(1), 0f, _ => tall);
            photo = Part(Middle, "Photo").sizeDelta;
            Assert.AreEqual(0.25f, photo.x / photo.y, 1e-3f);
            AssertBorderHugs(photo);
        }

        void AssertBorderHugs(Vector2 photo)
        {
            var edges = Middle.GetComponentsInChildren<Image>(true).Where(edge => edge.name == "Border").Select(edge => edge.rectTransform).ToArray();
            var outer = new Vector2(edges.Max(edge => edge.anchoredPosition.x + edge.sizeDelta.x * 0.5f), edges.Max(edge => edge.anchoredPosition.y + edge.sizeDelta.y * 0.5f));
            var width = edges.Min(edge => Mathf.Min(edge.sizeDelta.x, edge.sizeDelta.y));
            Assert.AreEqual(photo.x * 0.5f + width, outer.x, 1e-3f);
            Assert.AreEqual(photo.y * 0.5f + width, outer.y, 1e-3f);
        }

        [Test]
        public void The_fact_hangs_under_the_photo_whatever_its_shape()
        {
            var wide = Photo(400, 100);
            view.Show(Memories(1), 0f, _ => wide);
            var wideFact = Part(Middle, "Fact").position.y;
            Assert.That(wideFact, Is.LessThan(PhotoBottom()));
            var tall = Photo(100, 400);
            view.Show(Memories(1), 0f, _ => tall);
            Assert.That(Part(Middle, "Fact").position.y, Is.LessThan(PhotoBottom()));
            Assert.That(Part(Middle, "Fact").position.y, Is.LessThan(wideFact), "it follows the taller photo's lower edge");
        }

        float PhotoBottom()
        {
            var photo = Part(Middle, "Photo");
            return photo.position.y - photo.sizeDelta.y * 0.5f * photo.lossyScale.y;
        }

        [Test]
        public void A_card_shows_a_plain_panel_while_its_photo_is_missing()
        {
            view.Show(Memories(1), 0f, NoPhoto);
            Assert.IsTrue(Part(Middle, "Photo").gameObject.activeSelf);
            Assert.Less(Part(Middle, "Photo").GetComponent<RawImage>().color.a, 1f);
            var photo = Photo(8, 8);
            view.Show(Memories(1), 0f, _ => photo);
            Assert.AreEqual(1f, Part(Middle, "Photo").GetComponent<RawImage>().color.a);
        }

        [Test]
        public void Only_the_middle_card_shows_its_text()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            Assert.AreEqual(1f, Part(Middle, "Text").GetComponent<CanvasGroup>().alpha);
            var side = Drawn.Single(card => Shown(card, "Fact") == "Fact 6.");
            Assert.AreEqual(0f, Part(side, "Text").GetComponent<CanvasGroup>().alpha);
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
            Assert.AreEqual(5, view.Hit(eye, Part(Middle, "Photo").position * 0.6f, out _));
            var right = Drawn.Single(card => Shown(card, "Fact") == "Fact 6.");
            var outer = Part(right, "Photo");
            Assert.AreEqual(6, view.Hit(eye, (outer.position + outer.right * 0.03f) * 0.6f, out _));
            var left = Drawn.Single(card => Shown(card, "Fact") == "Fact 3.");
            var far = Part(left, "Photo");
            Assert.AreEqual(3, view.Hit(eye, (far.position - far.right * 0.05f) * 0.6f, out _));
        }

        [Test]
        public void A_pinch_two_centimetres_off_a_cards_photo_still_finds_it_and_four_does_not()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            var photo = Part(Middle, "Photo");
            var edge = photo.position + photo.right * (photo.sizeDelta.x * 0.5f * photo.lossyScale.x);
            Assert.AreEqual(5, view.Hit(Vector3.zero, (edge + photo.right * 0.015f) * 0.6f, out _));
            Assert.AreNotEqual(5, view.Hit(Vector3.zero, (edge + photo.right * 0.04f) * 0.6f, out _));
        }

        [Test]
        public void A_pinch_off_every_card_finds_none()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            Assert.AreEqual(-1, view.Hit(Vector3.zero, new Vector3(0f, 0.3f, 0.4f), out _));
            Assert.AreEqual(-1, view.Hit(Vector3.zero, new Vector3(0.5f, -0.03f, 0.3f), out _));
        }

        [Test]
        public void Where_cards_overlap_the_one_nearer_the_middle_is_found()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            var first = Part(Drawn.Single(card => Shown(card, "Fact") == "Fact 6."), "Photo");
            // The inner half of the second card on a side lies behind the first.
            var second = Part(Drawn.Single(card => Shown(card, "Fact") == "Fact 7."), "Photo");
            Assert.AreEqual(6, view.Hit(Vector3.zero, (second.position - second.right * 0.02f) * 0.6f, out _));
            Assert.AreEqual(6, view.Hit(Vector3.zero, first.position * 0.6f, out _));
        }

        [Test]
        public void A_notice_is_one_card_with_its_message_and_no_pinch_finds_it()
        {
            view.Show(Memories(10), 5f, NoPhoto);
            view.ShowNotice("Nothing collected yet", "Grab a snapshot.");
            Assert.AreEqual(1, Drawn.Length);
            var said = Middle.GetComponentsInChildren<Text>(true).Single(text => text.gameObject.activeInHierarchy).text;
            StringAssert.Contains("Nothing collected yet", said);
            StringAssert.Contains("Grab a snapshot.", said);
            Assert.IsFalse(Part(Middle, "Photo").gameObject.activeSelf);
            Assert.AreEqual(-1, view.Hit(Vector3.zero, Part(Middle, "Photo").position * 0.6f, out _));
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
        public void A_deck_folded_into_its_middle_draws_only_the_steady_card()
        {
            var memories = Memories(10);
            view.Show(memories, 5f, NoPhoto, 1f, 0f, memories[5]);
            Assert.AreEqual(1, Drawn.Length);
            Assert.AreEqual("Fact 5.", Shown(Middle, "Fact"));
        }

        [Test]
        public void A_deck_forming_fans_its_cards_out_from_the_middle_and_fades_them_in()
        {
            var memories = Memories(10);
            view.Show(memories, 5f, NoPhoto, 1f, 0.5f, memories[5]);
            var side = Drawn.Single(card => Shown(card, "Fact") == "Fact 6.");
            var formed = DeckLayout.Pose(1f);
            Assert.That(side.transform.localPosition.x, Is.GreaterThan(0f).And.LessThan(formed.Position.x));
            Assert.That(side.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.AreEqual(1f, Middle.GetComponent<CanvasGroup>().alpha, "the steady card does not fade");
        }

        [Test]
        public void A_skipped_memory_is_not_drawn()
        {
            var memories = Memories(10);
            view.Show(memories, 5f, NoPhoto, 1f, 1f, null, memories[5]);
            Assert.IsFalse(Drawn.Any(card => Shown(card, "Fact") == "Fact 5."));
            Assert.AreEqual(4, Drawn.Length);
        }

        [Test]
        public void A_hidden_deck_draws_nothing_and_draws_again()
        {
            var memories = Memories(4);
            view.Show(memories, 1f, NoPhoto);
            view.Hide();
            Assert.AreEqual(0, Drawn.Length);
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
