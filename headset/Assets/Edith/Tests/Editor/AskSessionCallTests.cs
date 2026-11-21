using Edith.Ask;
using NUnit.Framework;

namespace Edith.Tests
{
    /// A function EDITH calls, such as starting a hunt, must not end the question while it runs.
    public class AskSessionCallTests
    {
        AskSession session;

        [SetUp]
        public void SetUp()
        {
            session = new AskSession();
            session.Tap(0f);
            session.Ready(0.5f);
        }

        [Test]
        public void A_running_call_keeps_the_question_open_past_the_time_to_hear()
        {
            session.CallStarted();
            session.Tick(0.5f + AskSession.HearSeconds + 1f, 0);

            Assert.AreEqual(AskPhase.Listening, session.Phase);
            Assert.IsTrue(session.CallPending);
        }

        [Test]
        public void Without_a_call_the_same_wait_is_a_missed_question()
        {
            session.Tick(0.5f + AskSession.HearSeconds + 1f, 0);

            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.AreEqual(AskProblem.NotHeard, session.Problem);
        }

        [Test]
        public void A_turn_that_ends_while_a_call_runs_is_not_a_missed_question()
        {
            session.CallStarted();
            session.TurnComplete(1f);

            Assert.AreEqual(AskPhase.Listening, session.Phase);
        }

        [Test]
        public void The_spoken_result_is_waited_for_from_the_moment_the_result_goes_back()
        {
            session.CallStarted();
            session.CallFinished(6f);
            Assert.IsFalse(session.CallPending);

            session.Tick(6f + AskSession.HearSeconds - 1f, 0);
            Assert.AreEqual(AskPhase.Listening, session.Phase);
            session.AnswerAudio(7f);
            Assert.AreEqual(AskPhase.Answering, session.Phase);
        }

        [Test]
        public void An_answer_does_not_fade_while_a_call_runs_or_before_its_result_is_spoken()
        {
            session.AnswerAudio(1f);
            session.CallStarted();
            session.TurnComplete(2f);
            session.Tick(3f, speechLeft: 0);
            Assert.AreEqual(AskPhase.Answering, session.Phase, "the call is still running");

            session.CallFinished(4f);
            session.Tick(4.1f, speechLeft: 0);
            Assert.AreEqual(AskPhase.Answering, session.Phase, "the result has not been spoken yet");

            session.TurnComplete(6f);
            session.Tick(6.1f, speechLeft: 0);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
        }

        [Test]
        public void A_closed_session_forgets_its_calls()
        {
            session.CallStarted();
            session.Closed(1f);

            Assert.IsFalse(session.CallPending);
        }

        [Test]
        public void A_call_that_starts_after_the_question_ended_is_not_counted()
        {
            session.Fail(AskProblem.Dropped, 1f);
            session.CallStarted();

            Assert.IsFalse(session.CallPending);
        }
    }
}
