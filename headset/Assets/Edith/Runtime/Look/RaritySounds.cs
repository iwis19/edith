using UnityEngine;

namespace Edith.Look
{
    /// One short synthesized cue per rarity, played when a card reveals its rarity and again as it goes into the
    /// pocket. Each tier adds a note and rings longer than the one below, all at the same loudness. The clips live in
    /// Assets/Resources/RaritySounds.
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

        /// Plays the tier's cue from `point`, as plain 2D audio so the stereo cue arrives at full level wherever the
        /// card is. Used when a card reveals its rarity and again when it is pocketed.
        public static void Play(Rarity? tier, Vector3 point)
        {
            var clip = For(tier);
            if (clip == null) return;
            var voice = new GameObject("EDITH rarity sound").AddComponent<AudioSource>();
            voice.transform.position = point;
            voice.spatialBlend = 0f;
            voice.clip = clip;
            voice.Play();
            Object.Destroy(voice.gameObject, clip.length + 0.5f);
        }
    }
}
