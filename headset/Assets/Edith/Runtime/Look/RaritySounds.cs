using UnityEngine;

namespace Edith.Look
{
    /// One short synthesized cue per rarity, played as a card goes into the pocket. Each tier adds a note and rings
    /// longer than the one below, all at the same loudness. The clips live in Assets/Resources/RaritySounds.
    public static class RaritySounds
    {
        const string Folder = "RaritySounds/";

        /// A card that has no tier yet sounds common.
        public static AudioClip For(Rarity? tier)
        {
            var name = (tier ?? Rarity.Common).ToString().ToLowerInvariant();
            var clip = Resources.Load<AudioClip>(Folder + name);
            if (clip == null) Debug.LogWarning($"[EDITH] no rarity sound named {name}");
            return clip;
        }
    }
}
