using UnityEngine;

namespace Edith.Look
{
    /// Common grey, uncommon green, rare blue, epic purple, legendary gold, mythic red.
    public static class RarityColors
    {
        public static Color For(Rarity tier)
        {
            switch (tier)
            {
                case Rarity.Uncommon: return new Color(0.25f, 0.80f, 0.35f);
                case Rarity.Rare: return new Color(0.25f, 0.55f, 1.00f);
                case Rarity.Epic: return new Color(0.65f, 0.30f, 0.95f);
                case Rarity.Legendary: return new Color(1.00f, 0.80f, 0.20f);
                case Rarity.Mythic: return new Color(1.00f, 0.30f, 0.30f);
                default: return new Color(0.62f, 0.62f, 0.65f);
            }
        }
    }
}
