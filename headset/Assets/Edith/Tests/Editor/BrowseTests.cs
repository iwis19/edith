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

        static List<Chip> Chips(Browse browse)
        {
            var chips = new List<Chip>();
            browse.Chips(chips);
            return chips;
        }

        [Test]
        public void It_opens_showing_everything_from_no_tier_through_Common_to_Mythic_newest_first_within_a_tier()
        {
            var browse = Six();
            Assert.IsNull(browse.Filter);
            CollectionAssert.AreEqual(new[] { 3, 5, 1, 6, 4, 2 }, Ids(browse));
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
        public void A_tier_narrows_the_deck()
        {
            var browse = Six();
            browse.ToggleFilter(Rarity.Rare);
            CollectionAssert.AreEqual(new[] { 6, 4 }, Ids(browse));
            Assert.AreEqual(1, browse.IndexOf(4));
            Assert.AreEqual(-1, browse.IndexOf(5));
        }

        [Test]
        public void Choosing_the_tier_in_use_again_shows_everything_in_the_same_order()
        {
            var browse = Six();
            browse.ToggleFilter(Rarity.Rare);
            browse.ToggleFilter(Rarity.Rare);
            Assert.IsNull(browse.Filter);
            CollectionAssert.AreEqual(new[] { 3, 5, 1, 6, 4, 2 }, Ids(browse));
        }

        [Test]
        public void Choosing_the_selected_chip_again_clears_it_and_every_chip_is_unselected()
        {
            var browse = Six();
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
            browse.ToggleFilter(Rarity.Rare);
            browse.ToggleFilter(Rarity.Mythic);
            CollectionAssert.AreEqual(new[] { 2 }, Ids(browse));
        }

        [Test]
        public void Counts_do_not_change_with_the_filter()
        {
            var browse = Six();
            browse.ToggleFilter(Rarity.Mythic);
            Assert.AreEqual(2, browse.Count(Rarity.Rare));
        }

        [Test]
        public void The_chips_offer_every_tier_in_order_and_only_those_with_a_memory_are_enabled()
        {
            var browse = Six();
            var chips = Chips(browse);
            CollectionAssert.AreEqual(new[] { "Common 2", "Uncommon 0", "Rare 2", "Epic 0", "Legendary 0", "Mythic 1" }, Labels(chips));
            CollectionAssert.AreEqual(new[] { true, false, true, false, false, true }, chips.Select(chip => chip.Enabled).ToArray());
            Assert.IsTrue(chips.All(chip => !chip.Selected));
            Assert.AreEqual(Rarity.Rare, chips[2].Tier);
        }

        [Test]
        public void The_tier_in_use_is_the_selected_chip()
        {
            var browse = Six();
            browse.ToggleFilter(Rarity.Rare);
            var chips = Chips(browse);
            CollectionAssert.AreEqual(new[] { false, false, true, false, false, false }, chips.Select(chip => chip.Selected).ToArray());
        }

        [Test]
        public void A_filtered_deck_keeps_newest_first_within_its_tier()
        {
            var browse = Six();
            browse.ToggleFilter(Rarity.Common);
            CollectionAssert.AreEqual(new[] { 5, 1 }, Ids(browse));
        }

        [Test]
        public void The_deck_opens_at_the_first_card()
        {
            Assert.AreEqual(0, Six().IndexOf(3), "the memory with no tier is first");
            Assert.AreEqual(0, new Deck(Six().Visible.Count).Target);
        }

        [Test]
        public void No_memories_is_an_empty_deck()
        {
            var browse = new Browse(new Memory[0]);
            Assert.AreEqual(0, browse.Visible.Count);
            Assert.IsTrue(Chips(browse).All(chip => !chip.Enabled), "every tier chip is dimmed");
        }
    }
}
