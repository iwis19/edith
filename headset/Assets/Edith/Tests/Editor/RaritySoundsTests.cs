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

        /// Sign changes per second, which rises as a sound gets brighter.
        static float CrossingsPerSecond(float[] samples)
        {
            var crossings = 0;
            for (var i = 1; i < samples.Length; i++)
                if (samples[i - 1] < 0f != samples[i] < 0f) crossings++;
            return crossings * (float)RaritySounds.Rate / samples.Length;
        }

        [Test]
        public void Every_tier_is_longer_than_the_one_below_and_under_a_second()
        {
            var seconds = Tiers.Select(tier => RaritySounds.For(tier).Seconds).ToArray();

            for (var i = 1; i < seconds.Length; i++)
                Assert.Greater(seconds[i], seconds[i - 1], $"{Tiers[i]} against {Tiers[i - 1]}");
            Assert.Less(seconds.Last(), 1f);
        }

        [Test]
        public void Every_tier_is_brighter_than_the_one_below()
        {
            var brightness = Tiers.Select(tier => CrossingsPerSecond(RaritySounds.Render(RaritySounds.For(tier)))).ToArray();

            for (var i = 1; i < brightness.Length; i++)
                Assert.Greater(brightness[i], brightness[i - 1] * 1.05f, $"{Tiers[i]} against {Tiers[i - 1]}");
        }

        [Test]
        public void Every_tier_tops_out_on_a_higher_note_than_the_one_below()
        {
            var top = Tiers.Select(tier => RaritySounds.For(tier).Hertz.Max()).ToArray();

            for (var i = 1; i < top.Length; i++)
                Assert.Greater(top[i], top[i - 1], $"{Tiers[i]} against {Tiers[i - 1]}");
        }

        [Test]
        public void Every_sound_fills_its_length_at_the_same_peak_and_ends_in_silence()
        {
            foreach (var tier in Tiers)
            {
                var sound = RaritySounds.For(tier);
                var samples = RaritySounds.Render(sound);

                Assert.AreEqual(Mathf.RoundToInt(RaritySounds.Rate * sound.Seconds), samples.Length, tier.ToString());
                Assert.That(samples.Max(sample => Mathf.Abs(sample)), Is.EqualTo(RaritySounds.Peak).Within(1e-3f), tier.ToString());
                Assert.That(Mathf.Abs(samples[0]), Is.LessThan(1e-3f), tier.ToString());
                Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(1e-3f), tier.ToString());
            }
        }

        [Test]
        public void Only_mythic_shimmers()
        {
            foreach (var tier in Tiers)
                Assert.AreEqual(tier == Rarity.Mythic, RaritySounds.For(tier).ShimmerHertz > 0f, tier.ToString());
        }

        [Test]
        public void A_card_with_no_tier_sounds_common()
        {
            CollectionAssert.AreEqual(RaritySounds.Render(RaritySounds.For(Rarity.Common)), RaritySounds.Render(RaritySounds.For(null)));
        }
    }
}
