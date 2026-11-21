using System;
using System.Collections.Generic;
using System.Linq;
using Edith.Look;
using Edith.Scrapbook;
using NUnit.Framework;

namespace Edith.Tests
{
    public class BrowsePlacesTests
    {
        const string WesternPlace = "Western University, London, Ontario, Canada";
        const string ParkPlace = "Victoria Park, London, Ontario, Canada";
        const string TowerPlace = "CN Tower, Toronto, Ontario, Canada";
        static readonly GeoPoint Western = new GeoPoint(43.00542, -81.27480);
        static readonly GeoPoint Park = new GeoPoint(42.9885, -81.2490);
        static readonly GeoPoint Tower = new GeoPoint(43.6426, -79.3871);

        static Memory Made(int id, int day, string place, GeoPoint? point, Rarity? tier = null) =>
            new Memory(id, $"Thing {id}", null, tier, place, new DateTime(2026, 11, day), point);

        /// Newest first: two at Western, the tower, the park, an older one at Western, and one with no place.
        static Browse Travels() => new Browse(new[]
        {
            Made(6, 21, WesternPlace, Western, Rarity.Rare),
            Made(5, 21, WesternPlace, Western),
            Made(4, 20, TowerPlace, Tower, Rarity.Rare),
            Made(3, 19, ParkPlace, Park),
            Made(2, 18, WesternPlace, Western, Rarity.Common),
            Made(1, 12, null, null),
        });

        static int[] Ids(Browse browse) => browse.Visible.Select(memory => memory.Id).ToArray();

        static List<Chip> Chips(Browse browse)
        {
            var chips = new List<Chip>();
            browse.Chips(chips);
            return chips;
        }

        static string[] Labels(Browse browse) => Chips(browse).Select(chip => chip.Label).ToArray();

        static City London(Browse browse) => browse.Atlas.Cities.Single(city => city.Label == "London");

        [Test]
        public void With_a_place_it_opens_on_the_places_lens_at_the_globe_showing_everything()
        {
            var browse = Travels();
            Assert.AreEqual(Lens.Places, browse.Lens);
            Assert.IsNull(browse.City);
            Assert.IsNull(browse.Spot);
            CollectionAssert.AreEqual(new[] { 6, 5, 4, 3, 2, 1 }, Ids(browse));
        }

        [Test]
        public void At_the_globe_there_is_a_chip_for_each_city_with_its_count()
        {
            var chips = Chips(Travels());
            CollectionAssert.AreEqual(new[] { "London 4", "Toronto 1" }, chips.Select(chip => chip.Label).ToArray());
            Assert.AreEqual(ChipKind.City, chips[0].Kind);
            Assert.AreEqual("London", chips[0].City.Label);
        }

        [Test]
        public void Diving_into_a_city_shows_only_what_was_collected_there()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            Assert.AreEqual("London", browse.City.Label);
            CollectionAssert.AreEqual(new[] { 6, 5, 3, 2 }, Ids(browse));
        }

        [Test]
        public void In_a_city_there_is_a_globe_chip_and_a_chip_for_each_spot()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            var chips = Chips(browse);
            CollectionAssert.AreEqual(new[] { "Globe", "Western University 3", "Victoria Park 1" }, chips.Select(chip => chip.Label).ToArray());
            Assert.AreEqual(ChipKind.Globe, chips[0].Kind);
            Assert.AreEqual(ChipKind.Spot, chips[1].Kind);
            Assert.AreEqual("Western University", chips[1].Spot.Label);
            Assert.IsFalse(chips.Any(chip => chip.Selected));
        }

        [Test]
        public void A_spot_narrows_the_deck_to_what_was_found_there()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            browse.ToggleSpot(browse.City.Spots[1]);
            CollectionAssert.AreEqual(new[] { 3 }, Ids(browse));
            CollectionAssert.AreEqual(new[] { false, false, true }, Chips(browse).Select(chip => chip.Selected).ToArray());
        }

        [Test]
        public void Choosing_the_spot_in_use_again_shows_the_whole_city()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            browse.ToggleSpot(browse.City.Spots[0]);
            browse.ToggleSpot(browse.City.Spots[0]);
            Assert.IsNull(browse.Spot);
            CollectionAssert.AreEqual(new[] { 6, 5, 3, 2 }, Ids(browse));
        }

        [Test]
        public void Another_spot_replaces_the_one_in_use()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            browse.ToggleSpot(browse.City.Spots[1]);
            browse.ToggleSpot(browse.City.Spots[0]);
            CollectionAssert.AreEqual(new[] { 6, 5, 2 }, Ids(browse));
        }

        [Test]
        public void Zooming_out_to_the_globe_shows_everything_again()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            browse.ToggleSpot(browse.City.Spots[1]);
            browse.Surface();
            Assert.IsNull(browse.City);
            Assert.IsNull(browse.Spot);
            Assert.AreEqual(6, browse.Visible.Count);
            CollectionAssert.AreEqual(new[] { "London 4", "Toronto 1" }, Labels(browse));
        }

        [Test]
        public void Leaving_the_places_lens_returns_to_the_globe()
        {
            var browse = Travels();
            browse.Dive(London(browse));
            browse.SetLens(Lens.Rarity);
            Assert.AreEqual(6, browse.Visible.Count);
            browse.SetLens(Lens.Places);
            Assert.IsNull(browse.City);
            Assert.AreEqual(6, browse.Visible.Count);
        }

        [Test]
        public void A_spot_cannot_be_chosen_at_the_globe()
        {
            var browse = Travels();
            browse.ToggleSpot(London(browse).Spots[0]);
            Assert.IsNull(browse.Spot);
            Assert.AreEqual(6, browse.Visible.Count);
        }

        [Test]
        public void Diving_from_another_lens_switches_to_the_places_lens()
        {
            var browse = Travels();
            browse.SetLens(Lens.Rarity);
            browse.ToggleFilter(Rarity.Rare);
            browse.Dive(London(browse));
            Assert.AreEqual(Lens.Places, browse.Lens);
            Assert.IsNull(browse.Filter);
            CollectionAssert.AreEqual(new[] { 6, 5, 3, 2 }, Ids(browse));
        }

        [Test]
        public void A_long_place_name_is_cut_short_on_its_chip()
        {
            var browse = new Browse(new[] { Made(1, 21, "Fanshawe Pioneer Village Museum, London, Ontario, Canada", Western) });
            browse.Dive(browse.Atlas.Cities[0]);
            Assert.AreEqual("Fanshawe Pioneer… 1", Labels(browse)[1]);
            Assert.AreEqual("Fanshawe Pioneer Village Museum", browse.City.Spots[0].Label);
        }

        [Test]
        public void More_cities_than_fit_show_the_newest_few()
        {
            var memories = Enumerable.Range(0, 6)
                .Select(i => Made(6 - i, 20 - i, $"Spot {i}, Town {i}, Region", new GeoPoint(10 + i, 20)))
                .ToArray();
            CollectionAssert.AreEqual(new[] { "Town 0 1", "Town 1 1", "Town 2 1", "Town 3 1" }, Labels(new Browse(memories)));
        }
    }
}
