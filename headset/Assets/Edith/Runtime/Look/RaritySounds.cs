using UnityEngine;

namespace Edith.Look
{
    /// The sound of a card going into the pocket: plucked notes climbing a C major chord.
    public readonly struct RaritySound
    {
        /// The notes, one after another StepSeconds apart. Each rings on under the later ones.
        public readonly float[] Hertz;
        public readonly float StepSeconds;
        public readonly float Seconds;
        /// How fast a note dies away, per second.
        public readonly float Decay;
        /// The level of the second and third harmonics over the fundamental, from 0 for a pure tone.
        public readonly float Shine;
        /// Above 0, every note is doubled this many hertz higher, and the two beat against each other.
        public readonly float ShimmerHertz;

        public RaritySound(float[] hertz, float stepSeconds, float seconds, float decay, float shine, float shimmerHertz = 0f)
        {
            Hertz = hertz;
            StepSeconds = stepSeconds;
            Seconds = seconds;
            Decay = decay;
            Shine = shine;
            ShimmerHertz = shimmerHertz;
        }
    }

    /// One sound per rarity. Each tier adds a higher note, rings longer and has more harmonics than the tier below,
    /// and only mythic shimmers.
    public static class RaritySounds
    {
        public const int Rate = 48000;
        /// Every sound is scaled to this peak, so none is louder than another.
        public const float Peak = 0.4f;
        const float AttackSeconds = 0.005f;
        const float FadeOutSeconds = 0.03f;
        const float C5 = 523.25f, E5 = 659.25f, G5 = 783.99f, C6 = 1046.5f, E6 = 1318.5f, G6 = 1568f;

        /// A card that has no tier yet sounds common.
        public static RaritySound For(Rarity? tier)
        {
            switch (tier ?? Rarity.Common)
            {
                case Rarity.Uncommon: return new RaritySound(new[] { C5, E5 }, 0.08f, 0.30f, 14f, 0.1f);
                case Rarity.Rare: return new RaritySound(new[] { C5, E5, G5 }, 0.08f, 0.42f, 11f, 0.2f);
                case Rarity.Epic: return new RaritySound(new[] { C5, E5, G5, C6 }, 0.08f, 0.56f, 9f, 0.3f);
                case Rarity.Legendary: return new RaritySound(new[] { C5, E5, G5, C6, E6 }, 0.075f, 0.72f, 7f, 0.4f);
                case Rarity.Mythic: return new RaritySound(new[] { C5, E5, G5, C6, E6, G6 }, 0.07f, 0.95f, 4.5f, 0.5f, 5f);
                default: return new RaritySound(new[] { C5 }, 0.08f, 0.20f, 16f, 0f);
            }
        }

        /// The sound as mono samples at Rate, starting and ending at silence.
        public static float[] Render(in RaritySound sound)
        {
            var samples = new float[Mathf.RoundToInt(Rate * sound.Seconds)];
            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)Rate;
                var sum = 0f;
                for (var note = 0; note < sound.Hertz.Length; note++)
                {
                    var local = t - note * sound.StepSeconds;
                    if (local < 0f) break;
                    var envelope = Mathf.Clamp01(local / AttackSeconds) * Mathf.Exp(-local * sound.Decay);
                    sum += envelope * Voice(sound, sound.Hertz[note], local);
                }
                samples[i] = sum * Mathf.Clamp01((sound.Seconds - t) / FadeOutSeconds);
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            if (peak > 0f)
                for (var i = 0; i < samples.Length; i++) samples[i] *= Peak / peak;
            return samples;
        }

        static float Voice(in RaritySound sound, float hertz, float t)
        {
            var phase = 2f * Mathf.PI * hertz * t;
            var voice = Mathf.Sin(phase) + sound.Shine * (0.5f * Mathf.Sin(2f * phase) + 0.25f * Mathf.Sin(3f * phase));
            if (sound.ShimmerHertz <= 0f) return voice;
            return 0.5f * (voice + Mathf.Sin(2f * Mathf.PI * (hertz + sound.ShimmerHertz) * t));
        }
    }
}
