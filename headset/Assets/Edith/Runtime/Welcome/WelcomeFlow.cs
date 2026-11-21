using System.Collections.Generic;

namespace Edith.Welcome
{
    /// The three steps of the first-launch welcome. Done is not a step the person sees.
    public enum WelcomeStep { Interests = 1, Pair = 2, Practice = 3, Done = 4 }

    /// How EDITH sounds. Each maps to one of Gemini Live's voices through WelcomeStore.VoiceName.
    public enum VoiceStyle { Enthusiastic, Calm, Light }

    /// Where the welcome is and what the person has picked, as a function of what happened. It knows nothing of
    /// drawing, hands or the network: the controller feeds it taps and the pairing result and draws what it says.
    /// A phone that is already paired skips the pairing step, however the person got there.
    public sealed class WelcomeFlow
    {
        public const int StepCount = 3;

        /// The twelve interests offered, in the order they are drawn.
        public static readonly string[] AllInterests =
        {
            "Architecture", "Nature", "Food", "History", "Art", "Animals",
            "Tech", "Music", "Sports", "Design", "Science", "Cars",
        };

        readonly List<string> interests = new List<string>();
        bool phoneReady;

        public WelcomeStep Step { get; private set; } = WelcomeStep.Interests;

        /// The voice picked, Light until the person picks another.
        public VoiceStyle Voice { get; private set; } = VoiceStyle.Light;

        /// The person left the pairing step without pairing, so the welcome ended before the practice Look.
        public bool Skipped { get; private set; }

        /// Where the phone is, once it has said; null while the place is unknown.
        public string Place { get; private set; }

        public bool Finished => Step == WelcomeStep.Done;

        /// "1 OF 3" and so on, or empty once finished.
        public string Counter => Finished ? "" : (int)Step + " OF " + StepCount;

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

        /// Next on the first step: on to pairing, or straight to the practice when the phone is already paired.
        public void Next()
        {
            if (Step != WelcomeStep.Interests) return;
            Step = phoneReady ? WelcomeStep.Practice : WelcomeStep.Pair;
        }

        /// The backend has a phone position. `place` may be null while the place is still being found.
        public void PhoneConnected(string place)
        {
            phoneReady = true;
            if (!string.IsNullOrEmpty(place)) Place = place;
            if (Step == WelcomeStep.Pair) Step = WelcomeStep.Practice;
        }

        /// "Skip for now" on the pairing step. The welcome ends there: the practice Look needs a paired phone, since
        /// every gesture stays off until one is.
        public void Skip()
        {
            if (Step != WelcomeStep.Pair) return;
            Skipped = true;
            Step = WelcomeStep.Done;
        }

        /// "Back to step 1" on the practice.
        public void Back()
        {
            if (Step == WelcomeStep.Practice) Step = WelcomeStep.Interests;
        }

        /// The person drew their first box.
        public void PracticeDone()
        {
            if (Step == WelcomeStep.Practice) Step = WelcomeStep.Done;
        }
    }
}
