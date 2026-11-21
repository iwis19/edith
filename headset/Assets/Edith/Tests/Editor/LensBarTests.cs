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
    public class LensBarTests
    {
        GameObject parent;
        LensBar bar;
        Browse browse;
        readonly List<Chip> tabs = new List<Chip>(), chips = new List<Chip>();

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Deck");
            bar = new LensBar(parent.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
            browse = new Browse(new[]
            {
                new Memory(3, "Kettle", null, Rarity.Epic, null, new DateTime(2026, 11, 21)),
                new Memory(2, "Fern", null, Rarity.Common, null, new DateTime(2026, 11, 20)),
                new Memory(1, "Mug", null, Rarity.Epic, null, new DateTime(2026, 11, 18)),
            });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(parent);

        void Show(DateTime? centred = null)
        {
            browse.Tabs(tabs);
            browse.Chips(centred, chips);
            bar.Show(tabs, chips);
        }

        /// The world position of the chip that says `label`.
        Vector3 At(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).transform.position;

        Image Backing(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).GetComponentInParent<Image>();

        Color Ink(string label) => parent.GetComponentsInChildren<Text>().Single(text => text.text == label).color;

        bool Pinch(string label, out Chip chip) => bar.Hit(Vector3.zero, At(label) * 0.7f, out chip);

        [Test]
        public void The_time_lens_shows_its_tabs_and_days()
        {
            Show(new DateTime(2026, 11, 20));
            CollectionAssert.AreEqual(new[] { "Places", "Time", "Rarity", "11/21", "11/20", "11/18" }, bar.Labels);
        }

        [Test]
        public void The_rarity_lens_shows_all_six_tiers_with_their_counts()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            CollectionAssert.AreEqual(
                new[] { "Places", "Time", "Rarity", "Common 1", "Uncommon 0", "Rare 0", "Epic 2", "Legendary 0", "Mythic 0" }, bar.Labels);
        }

        [Test]
        public void Chips_left_over_from_another_lens_are_gone()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            browse.SetLens(Lens.Time);
            Show(new DateTime(2026, 11, 21));
            Assert.IsFalse(parent.GetComponentsInChildren<Text>().Any(text => text.text == "Epic 2"));
        }

        [Test]
        public void A_pinch_in_line_with_a_day_chooses_that_day()
        {
            Show(new DateTime(2026, 11, 21));
            Assert.IsTrue(Pinch("11/18", out var chip));
            Assert.AreEqual(ChipKind.Date, chip.Kind);
            Assert.AreEqual(new DateTime(2026, 11, 18), chip.Date);
        }

        [Test]
        public void A_pinch_in_line_with_a_tab_chooses_that_lens()
        {
            Show();
            Assert.IsTrue(Pinch("Rarity", out var chip));
            Assert.AreEqual(ChipKind.Tab, chip.Kind);
            Assert.AreEqual(Lens.Rarity, chip.Lens);
        }

        [Test]
        public void A_pinch_in_line_with_a_tier_chooses_that_tier()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            Assert.IsTrue(Pinch("Epic 2", out var chip));
            Assert.AreEqual(ChipKind.Tier, chip.Kind);
            Assert.AreEqual(Rarity.Epic, chip.Tier);
        }

        [Test]
        public void A_tier_with_no_memory_is_dimmed_and_a_pinch_on_it_chooses_nothing()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            Assert.IsFalse(Pinch("Mythic 0", out _));
            Assert.IsFalse(Pinch("Uncommon 0", out _));
            Assert.That(Backing("Mythic 0").color.a, Is.LessThan(Backing("Epic 2").color.a));
            Assert.That(Ink("Mythic 0").a, Is.LessThan(Ink("Epic 2").a));
        }

        [Test]
        public void The_selected_tier_is_filled_with_its_colour_and_the_others_are_not()
        {
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Epic);
            Show();
            var filled = Backing("Epic 2").color;
            Assert.AreEqual(RarityColors.For(Rarity.Epic).r, filled.r, 1e-4f);
            Assert.AreEqual(RarityColors.For(Rarity.Epic).b, filled.b, 1e-4f);
            Assert.AreNotEqual(filled, Backing("Common 1").color);
        }

        [Test]
        public void The_selected_tab_is_marked()
        {
            Show();
            Assert.AreNotEqual(Backing("Time").color, Backing("Rarity").color);
            Assert.AreEqual(Backing("Places").color, Backing("Rarity").color, "only the tab in use stands out");
        }

        [Test]
        public void Pinching_a_chip_by_its_text_when_all_six_tiers_show_still_picks_the_right_one()
        {
            browse.SetLens(Lens.Rarity);
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
            browse.SetLens(Lens.Rarity);
            Show();
            Assert.That(Mathf.Abs(At("Common 1").x), Is.LessThan(0.17f));
            Assert.That(Mathf.Abs(At("Mythic 0").x), Is.LessThan(0.17f));
            Assert.That(At("Mythic 0").x - At("Common 1").x, Is.GreaterThan(0.15f), "and do not overlap");
        }

        [Test]
        public void A_pinch_off_the_bar_chooses_nothing()
        {
            Show();
            Assert.IsFalse(bar.Hit(Vector3.zero, new Vector3(0f, 0f, 0.3f), out _), "straight ahead is the deck, not the bar");
            Assert.IsFalse(bar.Hit(Vector3.zero, new Vector3(0.3f, -0.15f, 0.3f), out _));
        }

        [Test]
        public void Chips_do_not_touch_each_other()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            var between = (At("Common 1") + At("Epic 2")) * 0.5f;
            Assert.IsTrue(Pinch("Common 1", out var left));
            Assert.IsTrue(Pinch("Epic 2", out var right));
            Assert.AreNotEqual(left.Tier, right.Tier);
            Assert.That(Vector3.Distance(At("Common 1"), At("Epic 2")), Is.GreaterThan(0.04f));
            Assert.That(between.y, Is.LessThan(At("Rarity").y), "the chips sit under the tabs");
        }

        [Test]
        public void The_tabs_sit_just_above_the_middle_card_and_the_chips_below_the_deck()
        {
            Show(new DateTime(2026, 11, 21));
            var middle = DeckLayout.Pose(0f).Position;
            var deckTop = middle.y + 0.105f;
            var deckBottom = middle.y - 0.105f;
            foreach (var tab in new[] { "Places", "Time", "Rarity" })
            {
                Assert.That(At(tab).y, Is.InRange(deckTop + 0.01f, deckTop + 0.06f), tab);
                Assert.That(At(tab).z, Is.EqualTo(middle.z).Within(0.01f));
            }
            Assert.That(At("Time").x, Is.EqualTo(0f).Within(1e-3f), "the tabs are centred over the middle card");
            foreach (var day in new[] { "11/21", "11/20", "11/18" })
            {
                Assert.That(At(day).y, Is.LessThan(deckBottom), day);
                Assert.That(At(day).y, Is.GreaterThan(-0.3f));
                Assert.That(At(day).z, Is.InRange(0.35f, 0.5f));
            }
        }

        [Test]
        public void A_pinch_finds_a_tab_above_the_deck_and_a_chip_below_it()
        {
            Show(new DateTime(2026, 11, 21));
            Assert.IsTrue(Pinch("Time", out var tab));
            Assert.AreEqual(ChipKind.Tab, tab.Kind);
            Assert.IsTrue(Pinch("11/20", out var day));
            Assert.AreEqual(ChipKind.Date, day.Kind);
        }

        [Test]
        public void A_hidden_bar_chooses_nothing()
        {
            Show();
            var at = At("Rarity");
            bar.Hide();
            Assert.IsFalse(bar.Hit(Vector3.zero, at * 0.7f, out _));
            CollectionAssert.IsEmpty(bar.Labels);
        }
    }
}
