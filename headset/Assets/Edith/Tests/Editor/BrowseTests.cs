using System;
using System.Collections.Generic;
using System.Linq;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class BrowseTests
    {
        static DateTime Day(int day) => new DateTime(2026, 11, day);

        static Memory Made(int id, int day, Rarity? tier = null) => new Memory(id, $"Thing {id}", null, tier, null, Day(day));

        /// Newest first: two on the 21st, one on the 20th, two on the 18th, one on the 12th.
        static Browse Six() => new Browse(new[]
        {
            Made(6, 21, Rarity.Rare), Made(5, 21, Rarity.Common), Made(4, 20, Rarity.Rare),
            Made(3, 18), Made(2, 18, Rarity.Mythic), Made(1, 12, Rarity.Common),
        });

        static int[] Ids(Browse browse) => browse.Visible.Select(memory => memory.Id).ToArray();

        static string[] Labels(List<Chip> chips) => chips.Select(chip => chip.Label).ToArray();

        static List<Chip> Chips(Browse browse, DateTime? centred = null)
        {
            var chips = new List<Chip>();
            browse.Chips(centred, chips);
            return chips;
        }

        [Test]
        public void Without_any_place_it_opens_on_the_time_lens_showing_everything_newest_first()
        {
            var browse = Six();
            Assert.AreEqual(Lens.Time, browse.Lens);
            Assert.IsNull(browse.Filter);
            CollectionAssert.AreEqual(new[] { 6, 5, 4, 3, 2, 1 }, Ids(browse));
        }

        [Test]
        public void Each_day_with_a_memory_is_listed_once_newest_first()
        {
            CollectionAssert.AreEqual(new[] { Day(21), Day(20), Day(18), Day(12) }, Six().Dates);
        }

        [Test]
        public void A_day_leads_to_its_first_card()
        {
            var browse = Six();
            Assert.AreEqual(0, browse.IndexOfDate(Day(21)));
            Assert.AreEqual(3, browse.IndexOfDate(Day(18)));
            Assert.AreEqual(-1, browse.IndexOfDate(Day(19)));
        }

        [Test]
        public void Tiers_are_counted_and_a_memory_without_one_counts_for_none()
        {
            var browse = Six();
            Assert.AreEqual(2, browse.Count(Rarity.Common));
            Assert.AreEqual(2, browse.Count(Rarity.Rare));
            Assert.AreEqual(1, browse.Count(Rarity.Mythic));
            Assert.AreEqual(0, browse.Count(Rarity.Epic));
        }

        [Test]
        public void A_tier_narrows_the_deck_and_its_days()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            CollectionAssert.AreEqual(new[] { 6, 4 }, Ids(browse));
            CollectionAssert.AreEqual(new[] { Day(21), Day(20) }, browse.Dates);
            Assert.AreEqual(1, browse.IndexOf(4));
            Assert.AreEqual(-1, browse.IndexOf(5));
        }

        [Test]
        public void Choosing_the_tier_in_use_again_shows_everything()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            browse.ToggleFilter(Rarity.Rare);
            Assert.IsNull(browse.Filter);
            Assert.AreEqual(6, browse.Visible.Count);
        }

        [Test]
        public void Choosing_the_selected_chip_again_clears_it_and_every_chip_is_unselected()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Mythic);
            Assert.IsTrue(Chips(browse)[5].Selected);
            browse.ToggleFilter(Rarity.Mythic);
            Assert.AreEqual(6, browse.Visible.Count);
            Assert.IsFalse(Chips(browse).Any(chip => chip.Selected));
        }

        [Test]
        public void Another_tier_replaces_the_one_in_use()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            browse.ToggleFilter(Rarity.Mythic);
            CollectionAssert.AreEqual(new[] { 2 }, Ids(browse));
        }

        [Test]
        public void Counts_do_not_change_with_the_filter()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Mythic);
            Assert.AreEqual(2, browse.Count(Rarity.Rare));
        }

        [Test]
        public void Leaving_the_rarity_lens_clears_its_filter()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            browse.SetLens(Lens.Time);
            Assert.IsNull(browse.Filter);
            Assert.AreEqual(6, browse.Visible.Count);
        }

        [Test]
        public void There_is_a_tab_for_each_lens_and_the_one_in_use_is_selected()
        {
            var browse = Six();
            var tabs = new List<Chip>();
            browse.Tabs(tabs);
            CollectionAssert.AreEqual(new[] { "Places", "Time", "Rarity" }, Labels(tabs));
            CollectionAssert.AreEqual(new[] { false, true, false }, tabs.Select(tab => tab.Selected).ToArray());
            browse.SetLens(Lens.Rarity);
            browse.Tabs(tabs);
            CollectionAssert.AreEqual(new[] { false, false, true }, tabs.Select(tab => tab.Selected).ToArray());
        }

        [Test]
        public void The_time_lens_offers_the_days_and_selects_the_one_in_the_middle()
        {
            var chips = Chips(Six(), Day(18));
            CollectionAssert.AreEqual(new[] { "11/21", "11/20", "11/18", "11/12" }, Labels(chips));
            CollectionAssert.AreEqual(new[] { false, false, true, false, false, false }, chips.Select(chip => chip.Selected).ToArray());
            Assert.AreEqual(ChipKind.Date, chips[2].Kind);
            Assert.AreEqual(Day(18), chips[2].Date);
        }

        [Test]
        public void More_days_than_fit_show_the_five_around_the_one_in_the_middle()
        {
            var browse = new Browse(Enumerable.Range(0, 9).Select(i => Made(9 - i, 20 - i)).ToArray());
            CollectionAssert.AreEqual(new[] { "11/16", "11/15", "11/14", "11/13", "11/12" }, Labels(Chips(browse, Day(14))));
            CollectionAssert.AreEqual(new[] { "11/20", "11/19", "11/18", "11/17", "11/16" }, Labels(Chips(browse, Day(20))));
            CollectionAssert.AreEqual(new[] { "11/16", "11/15", "11/14", "11/13", "11/12" }, Labels(Chips(browse, Day(12))));
        }

        [Test]
        public void The_rarity_lens_offers_every_tier_in_order_and_only_those_with_a_memory_are_enabled()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            var chips = Chips(browse);
            CollectionAssert.AreEqual(new[] { "Common 2", "Uncommon 0", "Rare 2", "Epic 0", "Legendary 0", "Mythic 1" }, Labels(chips));
            CollectionAssert.AreEqual(new[] { true, false, true, false, false, true }, chips.Select(chip => chip.Enabled).ToArray());
            Assert.IsTrue(chips.All(chip => chip.Kind == ChipKind.Tier && !chip.Selected));
            Assert.AreEqual(Rarity.Rare, chips[2].Tier);
        }

        [Test]
        public void The_tier_in_use_is_the_selected_chip()
        {
            var browse = Six();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            var chips = Chips(browse);
            CollectionAssert.AreEqual(new[] { false, false, true, false, false, false }, chips.Select(chip => chip.Selected).ToArray());
        }

        [Test]
        public void No_memories_is_an_empty_deck()
        {
            var browse = new Browse(new Memory[0]);
            Assert.AreEqual(0, browse.Visible.Count);
            Assert.AreEqual(0, browse.Dates.Count);
            Assert.AreEqual(0, Chips(browse).Count);
        }
    }
}
