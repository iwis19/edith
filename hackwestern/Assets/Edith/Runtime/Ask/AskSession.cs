namespace Edith.Ask
{
    /// Idle: nothing open. Connecting: the backend is minting a session and the socket is opening. Listening: the
    /// session is ready and hears the microphone. Answering: the answer is playing and the microphone is closed.
    /// Fading: the session is closed and the card lingers before it fades.
    public enum AskPhase { Idle, Connecting, Listening, Answering, Fading }

    public enum AskProblem { None, NotConfigured, MicDenied, Unreachable, Refused, Dropped, TimedOut, NotHeard }

    /// One question at a time, as a state machine over time in seconds. A tap starts a question from Idle or Fading and
    /// cuts one short while it is under way; Gemini's automatic activity detection ends the spoken turn.
    public sealed class AskSession
    {
        public const float ChimeSeconds = 0.25f;
        public const float ConnectSeconds = 8f;
        public const float HearSeconds = 15f;
        public const float AnswerSeconds = 60f;
        public const float AnswerLingerSeconds = 8f;
        public const float ProblemLingerSeconds = 4f;

        float tappedAt;
        float phaseAt;
        float fadeAt;
        bool turnComplete;

        public AskPhase Phase { get; private set; }
        /// Why the last question got no answer; None while one is under way or after an answer.
        public AskProblem Problem { get; private set; }

        /// A Live session is open or opening.
        public bool Busy => Phase == AskPhase.Connecting || Phase == AskPhase.Listening || Phase == AskPhase.Answering;

        /// The microphone runs from just after the chime until the answer starts.
        public bool MicWanted(float now) =>
            (Phase == AskPhase.Connecting || Phase == AskPhase.Listening) && now >= tappedAt + ChimeSeconds;

        /// True when the tap starts a new question; false when it cuts the one under way short.
        public bool Tap(float now)
        {
            if (Busy)
            {
                Fade(now, AnswerLingerSeconds);
                return false;
            }
            tappedAt = now;
            turnComplete = false;
            Problem = AskProblem.None;
            Enter(AskPhase.Connecting, now);
            return true;
        }

        public void Ready(float now)
        {
            if (Phase == AskPhase.Connecting) Enter(AskPhase.Listening, now);
        }

        public void AnswerAudio(float now)
        {
            if (Phase == AskPhase.Listening) Enter(AskPhase.Answering, now);
        }

        public void TurnComplete(float now)
        {
            if (Phase == AskPhase.Answering) turnComplete = true;
            else if (Phase == AskPhase.Listening) Fail(AskProblem.NotHeard, now);
        }

        /// The server closed the session. An answer already playing plays out.
        public void Closed(float now)
        {
            if (Phase == AskPhase.Answering) turnComplete = true;
            else Fail(AskProblem.Dropped, now);
        }

        /// Ends the question under way with `problem`. Ignored when none is under way.
        public void Fail(AskProblem problem, float now)
        {
            if (!Busy) return;
            Problem = problem;
            Fade(now, ProblemLingerSeconds);
        }

        /// Drops everything at once, as when the card is swiped away.
        public void Dismiss(float now)
        {
            if (Phase != AskPhase.Idle) Enter(AskPhase.Idle, now);
        }

        /// `speechLeft` is how many answer samples are still to play.
        public void Tick(float now, int speechLeft)
        {
            var inPhase = now - phaseAt;
            switch (Phase)
            {
                case AskPhase.Connecting when inPhase > ConnectSeconds:
                    Fail(AskProblem.TimedOut, now);
                    break;
                case AskPhase.Listening when inPhase > HearSeconds:
                    Fail(AskProblem.NotHeard, now);
                    break;
                case AskPhase.Answering when (turnComplete && speechLeft <= 0) || inPhase > AnswerSeconds:
                    Fade(now, AnswerLingerSeconds);
                    break;
                case AskPhase.Fading when now >= fadeAt:
                    Enter(AskPhase.Idle, now);
                    break;
            }
        }

        /// The one short line a card shows for `problem`.
        public static string Describe(AskProblem problem)
        {
            switch (problem)
            {
                case AskProblem.NotConfigured: return "EDITH has no backend set up.";
                case AskProblem.MicDenied: return "Microphone access is off.";
                case AskProblem.Unreachable: return "Can't reach EDITH. Check the network.";
                case AskProblem.Refused: return "EDITH's server refused the question.";
                case AskProblem.Dropped: return "The connection dropped.";
                case AskProblem.TimedOut: return "EDITH took too long to answer.";
                case AskProblem.NotHeard: return "I didn't catch that.";
                default: return "";
            }
        }

        void Fade(float now, float linger)
        {
            fadeAt = now + linger;
            Enter(AskPhase.Fading, now);
        }

        void Enter(AskPhase phase, float now)
        {
            Phase = phase;
            phaseAt = now;
        }
    }
}
