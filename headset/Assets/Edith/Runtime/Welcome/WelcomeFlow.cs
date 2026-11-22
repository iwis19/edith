using System.Collections.Generic;

namespace Edith.Welcome
{
    /// The two steps of the first-launch welcome. Done is not a step the person sees.
    public enum WelcomeStep { Interests = 1, Pair = 2, Done = 3 }

    /// How EDITH sounds. Each maps to one of Gemini Live's voices through WelcomeStore.VoiceName.
    public enum VoiceStyle { Enthusiastic, Calm, Playful }

    /// Where the welcome is and what the person has picked, as a function of what happened. It knows nothing of
    /// drawing, hands or the network: the controller feeds it taps and the pairing result and draws what it says.
    /// A phone that is already paired skips the pairing step, however the person got there.
    public sealed class WelcomeFlow
    {
        /// The twelve interests offered, in the order they are drawn.
        public static readonly string[] AllInterests =
        {
            "Nature", "Food", "History", "Art", "Animals",
            "Tech", "Music", "Sports", "Science",
        };

        readonly List<string> interests = new List<string>();
        bool phoneReady;

        public WelcomeStep Step { get; private set; } = WelcomeStep.Interests;

        /// The voice picked, Playful until the person picks another.
        public VoiceStyle Voice { get; private set; } = VoiceStyle.Playful;

        public bool Finished => Step == WelcomeStep.Done;

        public IReadOnlyList<string> Interests => interests;

        public bool IsPicked(string interest) => interests.Contains(interest);

        /// Picks an interest or puts it back. False for a name that is not offered.
        public bool Toggle(string interest)
        {
            if (Step != WelcomeStep.Interests || System.Array.IndexOf(AllInterests, interest) < 0) return false;
            if (!interests.Remove(interest)) interests.Add(interest);
            return true;
        }

        public void ChooseVoice(VoiceStyle voice)
        {
            if (Step == WelcomeStep.Interests) Voice = voice;
        }

        /// Next on the first step: on to pairing, or done when the phone is already paired.
        public void Next()
        {
            if (Step != WelcomeStep.Interests) return;
            Step = phoneReady ? WelcomeStep.Done : WelcomeStep.Pair;
        }

        /// The backend has a phone position, which ends the pairing step.
        public void PhoneConnected()
        {
            phoneReady = true;
            if (Step == WelcomeStep.Pair) Step = WelcomeStep.Done;
        }

    }
}
