using UnityEngine;

namespace Edith.Welcome
{
    /// What the headset remembers from the welcome: that it ran, what the person likes and how EDITH should sound.
    public static class WelcomeStore
    {
        const string DoneKey = "edith.welcomeDone";
        const string InterestsKey = "edith.interests";
        const string VoiceKey = "edith.voice";
        const char Separator = '|';

        /// Gemini Live voices for the three styles: upbeat, gentle and playful.
        public static string VoiceName(VoiceStyle style)
        {
            switch (style)
            {
                case VoiceStyle.Enthusiastic: return "Puck";
                case VoiceStyle.Calm: return "Vindemiatrix";
                default: return "Callirrhoe";
            }
        }

        /// True once the welcome has run to its end, or been left at the pairing step.
        public static bool Done
        {
            get => PlayerPrefs.GetInt(DoneKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(DoneKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// The Gemini voice the person picked, or null before the welcome has saved a choice, which leaves the
        /// backend's own default.
        public static string Voice => PlayerPrefs.HasKey(VoiceKey) ? VoiceName((VoiceStyle)PlayerPrefs.GetInt(VoiceKey)) : null;

        /// The interests the person picked, in the order they picked them, or an empty array.
        public static string[] Interests =>
            PlayerPrefs.GetString(InterestsKey, "").Split(new[] { Separator }, System.StringSplitOptions.RemoveEmptyEntries);

        /// Remembers the picks the person made on the first step.
        public static void Save(WelcomeFlow flow)
        {
            PlayerPrefs.SetString(InterestsKey, string.Join(Separator.ToString(), flow.Interests));
            PlayerPrefs.SetInt(VoiceKey, (int)flow.Voice);
            PlayerPrefs.Save();
        }

        /// Forgets everything, so the welcome runs again from the first step: a new profile.
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(DoneKey);
            PlayerPrefs.DeleteKey(InterestsKey);
            PlayerPrefs.DeleteKey(VoiceKey);
            PlayerPrefs.Save();
        }
    }
}
