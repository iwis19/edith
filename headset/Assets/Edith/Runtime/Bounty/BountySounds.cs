using UnityEngine;

namespace Edith.Bounty
{
    public enum BountyCue { Tick, Find, Fanfare, TimeUp }

    /// The hunt's cues, made in code: a short tick for each ten seconds of the last thirty, a rising tone for a find,
    /// a fanfare when all five are found and a low tone for time up. Each play makes its clip fresh, because a clip
    /// made earlier plays nothing once the sound output has been reopened.
    public static class BountySounds
    {
        const int Rate = 48000;

        // Each note: pitch in hertz, start and length in seconds.
        static readonly (float hertz, float start, float length)[] NotesTick = { (1500f, 0f, 0.07f) };
        static readonly (float hertz, float start, float length)[] NotesFind =
            { (523.3f, 0f, 0.16f), (659.3f, 0.09f, 0.16f), (784f, 0.18f, 0.3f) };
        static readonly (float hertz, float start, float length)[] NotesFanfare =
        {
            (523.3f, 0f, 0.18f), (659.3f, 0.14f, 0.18f), (784f, 0.28f, 0.18f), (1046.5f, 0.42f, 0.5f),
            (784f, 0.42f, 0.5f), (659.3f, 0.42f, 0.5f),
        };
        static readonly (float hertz, float start, float length)[] NotesTimeUp =
            { (220f, 0f, 0.3f), (164.8f, 0.22f, 0.5f) };

        /// The cue's 48 kHz mono samples.
        public static float[] Samples(BountyCue cue)
        {
            var notes = cue == BountyCue.Tick ? NotesTick : cue == BountyCue.Find ? NotesFind
                : cue == BountyCue.Fanfare ? NotesFanfare : NotesTimeUp;
            var end = 0f;
            foreach (var note in notes) end = Mathf.Max(end, note.start + note.length);
            const float gain = 0.3f;
            var samples = new float[Mathf.CeilToInt((end + 0.1f) * Rate)];
            foreach (var note in notes)
            {
                var first = Mathf.RoundToInt(note.start * Rate);
                var count = Mathf.RoundToInt((note.length + 0.1f) * Rate);
                for (var i = 0; i < count && first + i < samples.Length; i++)
                {
                    var t = i / (float)Rate;
                    var envelope = Mathf.Clamp01(t * 300f) * Mathf.Exp(-t * 4.5f / note.length);
                    samples[first + i] += gain * envelope * Mathf.Sin(2f * Mathf.PI * note.hertz * t);
                }
            }
            return samples;
        }

        /// Plays the cue as plain 2D audio, so it arrives at full level wherever the head is.
        public static void Play(BountyCue cue)
        {
            if (!Application.isPlaying) return;
            var samples = Samples(cue);
            var clip = AudioClip.Create("EDITH bounty " + cue, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            var voice = new GameObject("EDITH bounty sound").AddComponent<AudioSource>();
            voice.spatialBlend = 0f;
            voice.clip = clip;
            voice.Play();
            Object.Destroy(voice.gameObject, clip.length + 0.5f);
            Object.Destroy(clip, clip.length + 0.5f);
        }
    }
}
