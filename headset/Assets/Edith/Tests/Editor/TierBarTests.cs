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
    public class TierBarTests
    {
        GameObject parent;
        TierBar bar;
        Browse browse;
        readonly List<Chip> chips = new List<Chip>();

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Deck");
            bar = new TierBar(parent.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
            browse = new Browse(new[]
            {
                new Memory(3, "Kettle", null, Rarity.Epic, null, new DateTime(2026, 11, 21)),
                new Memory(2, "Fern", null, Rarity.Common, null, new DateTime(2026, 11, 20)),
                new Memory(1, "Mug", null, Rarity.Epic, null, new DateTime(2026, 11, 18)),
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(parent);

        void Show()
        {
            browse.Chips(chips);
            bar.Show(chips);
        }

        /// The world position of the chip that says `label`.
        Vector3 At(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).transform.position;

        Image Backing(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).GetComponentInParent<Image>();

        Color Ink(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).color;

        bool Pinch(string label, out Chip chip) => bar.Hit(Vector3.zero, At(label) * 0.7f, out chip, out _);

        [Test]
        public void The_bar_shows_all_six_tiers_with_their_counts()
        {
            Show();
            CollectionAssert.AreEqual(
                new[] { "Common 1", "Uncommon 0", "Rare 0", "Epic 2", "Legendary 0", "Mythic 0" }, bar.Labels);
        }

        [Test]
        public void A_pinch_in_line_with_a_tier_chooses_that_tier()
        {
            Show();
            Assert.IsTrue(Pinch("Epic 2", out var chip));
            Assert.AreEqual(Rarity.Epic, chip.Tier);
        }

        [Test]
        public void A_tier_with_no_memory_is_dimmed_and_a_pinch_on_it_chooses_nothing()
        {
            Show();
            Assert.IsFalse(Pinch("Mythic 0", out _));
            Assert.IsFalse(Pinch("Uncommon 0", out _));
            Assert.That(Backing("Mythic 0").color.a, Is.LessThan(Backing("Epic 2").color.a));
            Assert.That(Ink("Mythic 0").a, Is.LessThan(Ink("Epic 2").a));
        }

        [Test]
        public void The_selected_tier_is_filled_with_its_colour_and_the_others_are_not()
        {
            browse.ToggleFilter(Rarity.Epic);
            Show();
            var filled = Backing("Epic 2").color;
            Assert.AreEqual(RarityColors.For(Rarity.Epic).r, filled.r, 1e-4f);
            Assert.AreEqual(RarityColors.For(Rarity.Epic).b, filled.b, 1e-4f);
            Assert.AreNotEqual(filled, Backing("Common 1").color);
        }

        [Test]
        public void Pinching_a_chip_by_its_text_when_all_six_tiers_show_still_picks_the_right_one()
        {
            Show();
            foreach (var (label, tier) in new[] { ("Common 1", Rarity.Common), ("Epic 2", Rarity.Epic) })
            {
                Assert.IsTrue(Pinch(label, out var chip));
                Assert.AreEqual(tier, chip.Tier);
            }
        }

        [Test]
        public void The_six_tier_chips_fit_the_width_of_the_deck()
        {
            Show();
            Assert.That(Mathf.Abs(At("Common 1").x), Is.LessThan(0.26f));
            Assert.That(Mathf.Abs(At("Mythic 0").x), Is.LessThan(0.26f));
            Assert.That(At("Mythic 0").x - At("Common 1").x, Is.GreaterThan(0.2f), "and do not overlap");
        }

        [Test]
        public void A_pinch_a_centimetre_off_a_chip_still_chooses_it_and_three_centimetres_off_does_not()
        {
            Show();
            var rect = parent.GetComponentsInChildren<Text>().Single(text => text.text == "Epic 2").GetComponentInParent<Image>().rectTransform;
            var edge = rect.position + rect.up * (rect.sizeDelta.y * 0.5f * rect.lossyScale.x);
            Assert.IsTrue(bar.Hit(Vector3.zero, (edge + rect.up * 0.01f) * 0.7f, out var chip, out _));
            Assert.AreEqual(Rarity.Epic, chip.Tier);
            Assert.IsFalse(bar.Hit(Vector3.zero, (edge + rect.up * 0.03f + rect.right * 0.3f) * 0.7f, out _, out _));
        }

        [Test]
        public void A_pinch_off_the_bar_chooses_nothing()
        {
            Show();
            Assert.IsFalse(bar.Hit(Vector3.zero, new Vector3(0f, 0f, 0.3f), out _, out _), "straight ahead is the deck, not the bar");
            Assert.IsFalse(bar.Hit(Vector3.zero, new Vector3(0.3f, -0.15f, 0.3f), out _, out _));
        }

        [Test]
        public void Chips_do_not_touch_each_other()
        {
            Show();
            Assert.IsTrue(Pinch("Common 1", out var left));
            Assert.IsTrue(Pinch("Epic 2", out var right));
            Assert.AreNotEqual(left.Tier, right.Tier);
            Assert.That(Vector3.Distance(At("Common 1"), At("Epic 2")), Is.GreaterThan(0.04f));
        }

        [Test]
        public void The_chips_are_under_the_deck_within_reach()
        {
            Show();
            var deckBottom = DeckLayout.Pose(0f).Position.y - 0.105f;
            foreach (var label in new[] { "Common 1", "Uncommon 0", "Rare 0", "Epic 2", "Legendary 0", "Mythic 0" })
            {
                Assert.That(At(label).y, Is.LessThan(deckBottom), label);
                Assert.That(At(label).y, Is.GreaterThan(-0.3f), label);
                Assert.That(At(label).z, Is.InRange(0.6f, 0.7f), label);
            }
        }

        [Test]
        public void A_hidden_bar_chooses_nothing()
        {
            Show();
            var at = At("Epic 2");
            bar.Hide();
            Assert.IsFalse(bar.Hit(Vector3.zero, at * 0.7f, out _, out _));
            CollectionAssert.IsEmpty(bar.Labels);
        }
    }
}
