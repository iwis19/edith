using System.Linq;
using Edith.Look;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class RaritySoundsTests
    {
        static readonly Rarity[] Tiers =
            { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary, Rarity.Mythic };

        [Test]
        public void Every_tier_has_a_clip()
        {
            foreach (var tier in Tiers) Assert.IsNotNull(RaritySounds.For(tier), tier.ToString());
        }

        [Test]
        public void Every_tier_is_longer_than_the_one_below_and_no_longer_than_two_seconds()
        {
            var seconds = Tiers.Select(tier => RaritySounds.For(tier).length).ToArray();

            for (var i = 1; i < seconds.Length; i++)
                Assert.Greater(seconds[i], seconds[i - 1], $"{Tiers[i]} against {Tiers[i - 1]}");
            Assert.LessOrEqual(seconds.Last(), 2f);
        }

        [Test]
        public void A_card_with_no_tier_sounds_common()
        {
            Assert.AreSame(RaritySounds.For(Rarity.Common), RaritySounds.For(null));
        }
    }
}
