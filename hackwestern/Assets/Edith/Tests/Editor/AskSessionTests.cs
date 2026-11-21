using System.Collections.Generic;
using Edith.Ask;
using Edith.Snapshots;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class AskSessionTests
    {
        AskSession session;

        [SetUp]
        public void SetUp() => session = new AskSession();

        void Answered(float at = 3f)
        {
            session.Tap(0f);
            session.Ready(1f);
            session.AnswerAudio(2f);
            session.TurnComplete(at);
        }

        [Test]
        public void A_question_runs_from_tap_to_fade_to_idle()
        {
            Assert.IsTrue(session.Tap(0f));
            Assert.AreEqual(AskPhase.Connecting, session.Phase);
            session.Ready(0.6f);
            Assert.AreEqual(AskPhase.Listening, session.Phase);
            session.AnswerAudio(2f);
            Assert.AreEqual(AskPhase.Answering, session.Phase);
            session.TurnComplete(4f);
            session.Tick(4.1f, speechLeft: 24000);
            Assert.AreEqual(AskPhase.Answering, session.Phase, "the answer plays out after the turn ends");
            session.Tick(5f, speechLeft: 0);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.AreEqual(AskProblem.None, session.Problem);
            session.Tick(5f + AskSession.AnswerLingerSeconds - 0.1f, 0);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            session.Tick(5f + AskSession.AnswerLingerSeconds, 0);
            Assert.AreEqual(AskPhase.Idle, session.Phase);
        }

        [Test]
        public void The_microphone_opens_after_the_chime_and_closes_when_the_answer_starts()
        {
            session.Tap(0f);
            Assert.IsFalse(session.MicWanted(0.1f));
            Assert.IsTrue(session.MicWanted(AskSession.ChimeSeconds));
            session.Ready(0.5f);
            Assert.IsTrue(session.MicWanted(0.6f));
            session.AnswerAudio(1f);
            Assert.IsFalse(session.MicWanted(1.1f));
        }

        [Test]
        public void A_tap_while_busy_cuts_the_question_short_and_a_tap_while_fading_starts_another()
        {
            session.Tap(0f);
            session.Ready(1f);
            Assert.IsFalse(session.Tap(2f));
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.IsFalse(session.Busy);
            Assert.IsTrue(session.Tap(3f));
            Assert.AreEqual(AskPhase.Connecting, session.Phase);
        }

        [Test]
        public void Failures_linger_shorter_and_keep_their_reason()
        {
            session.Tap(0f);
            session.Fail(AskProblem.Unreachable, 1f);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.AreEqual(AskProblem.Unreachable, session.Problem);
            session.Tick(1f + AskSession.ProblemLingerSeconds, 0);
            Assert.AreEqual(AskPhase.Idle, session.Phase);
            session.Tap(10f);
            Assert.AreEqual(AskProblem.None, session.Problem, "a new question clears the old reason");
        }

        [Test]
        public void A_failure_after_the_question_ended_changes_nothing()
        {
            Answered();
            session.Tick(4f, 0);
            session.Fail(AskProblem.Dropped, 4.5f);
            Assert.AreEqual(AskProblem.None, session.Problem);
        }

        [Test]
        public void Each_waiting_phase_times_out()
        {
            session.Tap(0f);
            session.Tick(AskSession.ConnectSeconds + 0.1f, 0);
            Assert.AreEqual(AskProblem.TimedOut, session.Problem);

            session.Tap(20f);
            session.Ready(21f);
            session.Tick(21f + AskSession.HearSeconds + 0.1f, 0);
            Assert.AreEqual(AskProblem.NotHeard, session.Problem);

            session.Tap(50f);
            session.Ready(51f);
            session.AnswerAudio(52f);
            session.Tick(52f + AskSession.AnswerSeconds + 0.1f, 100);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.AreEqual(AskProblem.None, session.Problem);
        }

        [Test]
        public void A_turn_that_ends_with_no_answer_was_not_heard()
        {
            session.Tap(0f);
            session.Ready(1f);
            session.TurnComplete(3f);
            Assert.AreEqual(AskProblem.NotHeard, session.Problem);
        }

        [Test]
        public void A_closed_socket_drops_a_waiting_question_but_lets_an_answer_play_out()
        {
            session.Tap(0f);
            session.Closed(0.5f);
            Assert.AreEqual(AskProblem.Dropped, session.Problem);

            session.Tap(10f);
            session.Ready(11f);
            session.AnswerAudio(12f);
            session.Closed(13f);
            Assert.AreEqual(AskPhase.Answering, session.Phase);
            session.Tick(13.5f, 0);
            Assert.AreEqual(AskPhase.Fading, session.Phase);
            Assert.AreEqual(AskProblem.None, session.Problem);
        }

        [Test]
        public void Dismiss_goes_straight_to_idle_from_anywhere()
        {
            session.Tap(0f);
            session.Ready(1f);
            session.Dismiss(2f);
            Assert.AreEqual(AskPhase.Idle, session.Phase);
            Answered();
            session.Tick(4f, 0);
            session.Dismiss(5f);
            Assert.AreEqual(AskPhase.Idle, session.Phase);
        }

        [Test]
        public void Out_of_order_events_are_ignored()
        {
            session.AnswerAudio(0f);
            session.Ready(0f);
            session.TurnComplete(0f);
            Assert.AreEqual(AskPhase.Idle, session.Phase);
            session.Tap(1f);
            session.AnswerAudio(1.1f);
            Assert.AreEqual(AskPhase.Connecting, session.Phase, "audio cannot arrive before the session is ready");
        }

        [Test]
        public void Every_problem_has_a_line()
        {
            foreach (AskProblem problem in System.Enum.GetValues(typeof(AskProblem)))
                Assert.AreEqual(problem == AskProblem.None, AskSession.Describe(problem) == "", problem.ToString());
        }
    }

    public class AskGeometryTests
    {
        [Test]
        public void The_point_nearest_the_line_of_sight_wins_whatever_its_distance()
        {
            var points = new List<Vector3>
            {
                new Vector3(0.3f, 0f, 1f),
                new Vector3(0.5f, 0f, 5f),
                new Vector3(0f, 0f, -1f),
            };
            Assert.AreEqual(1, AskGeometry.NearestToGaze(Vector3.zero, Vector3.forward, points));
            Assert.AreEqual(2, AskGeometry.NearestToGaze(Vector3.zero, Vector3.back, points));
            Assert.AreEqual(-1, AskGeometry.NearestToGaze(Vector3.zero, Vector3.forward, new List<Vector3>()));
        }

        [Test]
        public void A_tap_in_line_with_a_card_hits_it_from_any_distance()
        {
            var box = new SightBox(new Pose(Vector3.zero, Quaternion.identity), Rect.MinMaxRect(-0.2f, -0.1f, 0.2f, 0.1f), 2f);
            var eye = new Vector3(0f, 0f, 0.5f);
            Assert.IsTrue(AskGeometry.InLine(box, eye, eye + new Vector3(0.05f, 0.02f, 0.4f), 0.02f));
            Assert.IsTrue(AskGeometry.InLine(box, eye, eye + new Vector3(0f, 0f, 0.05f), 0.02f));
            Assert.IsFalse(AskGeometry.InLine(box, eye, eye + new Vector3(0.3f, 0f, 0.4f), 0.02f), "off to the side");
            Assert.IsFalse(AskGeometry.InLine(box, eye, eye + new Vector3(0f, 0f, -0.4f), 0.02f), "behind the eye");
        }

        [TestCase(1280, 960, 512, 512, 384)]
        [TestCase(960, 1280, 512, 384, 512)]
        [TestCase(1000, 1000, 512, 512, 512)]
        [TestCase(300, 200, 512, 300, 200)]
        [TestCase(4000, 10, 512, 512, 1)]
        public void Photos_shrink_to_the_long_side_limit_and_never_grow(int w, int h, int max, int ew, int eh)
        {
            Assert.AreEqual(new Vector2Int(ew, eh), AskGeometry.FitLongSide(w, h, max));
        }
    }
}
