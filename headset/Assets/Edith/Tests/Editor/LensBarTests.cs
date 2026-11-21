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

        bool Pinch(string label, out Chip chip) => bar.Hit(Vector3.zero, At(label) * 0.7f, out chip);

        [Test]
        public void The_time_lens_shows_its_tabs_and_days()
        {
            Show(new DateTime(2026, 11, 20));
            CollectionAssert.AreEqual(new[] { "Places", "Time", "Rarity", "11/21", "11/20", "11/18" }, bar.Labels);
        }

        [Test]
        public void The_rarity_lens_shows_all_and_its_tiers()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            CollectionAssert.AreEqual(new[] { "Places", "Time", "Rarity", "All 3", "Common 1", "Epic 2" }, bar.Labels);
        }

        [Test]
        public void Chips_left_over_from_another_lens_are_gone()
        {
            browse.SetLens(Lens.Rarity);
            Show();
            browse.SetLens(Lens.Time);
            Show(new DateTime(2026, 11, 21));
            Assert.IsFalse(parent.GetComponentsInChildren<Text>().Any(text => text.text == "All 3"));
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
            Assert.IsTrue(Pinch("All 3", out chip));
            Assert.AreEqual(ChipKind.AllTiers, chip.Kind);
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
        public void The_bar_sits_under_the_cards_within_reach()
        {
            Show();
            var deckBottom = DeckLayout.Pose(0f).Position.y - 0.105f;
            Assert.That(At("Time").y, Is.LessThan(deckBottom));
            Assert.That(At("11/21").y, Is.GreaterThan(-0.3f));
            Assert.That(At("11/21").z, Is.InRange(0.35f, 0.5f));
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
