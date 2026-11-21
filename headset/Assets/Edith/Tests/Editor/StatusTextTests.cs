using Edith.Capture;
using Edith.Diagnostics;
using Edith.Grab;
using Edith.Hands;
using Edith.Pairing;
using Edith.Pinch;
using NUnit.Framework;
using UnityEngine;

namespace Edith.Tests
{
    public class StatusTextTests
    {
        static PinchReading Pinch(PinchPhase phase, PinchEvent pinchEvent = PinchEvent.None, PinchRejection rejection = PinchRejection.None) =>
            new PinchReading(phase, pinchEvent, rejection, false, default, Vector3.zero);

        static GrabReading Grab(GrabPhase phase, GrabEvent grabEvent = GrabEvent.None, int card = GrabTracker.NoCard) =>
            new GrabReading(phase, grabEvent, card, true, Vector3.zero, Quaternion.identity, phase == GrabPhase.Holding);

        static StatusView View(StatusTally tally, float now = 100f) => new StatusView
        {
            Tally = tally,
            Camera = CameraStatus.Streaming,
            HasPairing = true,
            Pairing = PairPhase.Paired,
            BackendHost = "192.168.0.7:8000",
            Backend = new BackendHealth(BackendReach.Up, "85 ms", 88f),
            Now = now,
        };

        [Test]
        public void The_tally_counts_taps_and_confirmed_looks_only()
        {
            var tally = new StatusTally();
            tally.OnPinch(Pinch(PinchPhase.Idle, PinchEvent.Tap), 1f);
            tally.OnPinch(Pinch(PinchPhase.Idle, PinchEvent.Tap), 2f);
            tally.OnPinch(Pinch(PinchPhase.Released, PinchEvent.Completed), 3f);
            tally.OnPinch(Pinch(PinchPhase.Idle, PinchEvent.Confirmed), 4f);
            tally.OnPinch(Pinch(PinchPhase.Idle, PinchEvent.Cancelled, PinchRejection.TooSmall), 5f);

            Assert.AreEqual(2, tally.Taps);
            Assert.AreEqual(1, tally.Looks);
        }

        [Test]
        public void The_tally_keeps_the_last_rejection_and_when_it_came()
        {
            var tally = new StatusTally();
            tally.OnPinch(Pinch(PinchPhase.Idle, PinchEvent.Cancelled, PinchRejection.TooClose), 10f);
            tally.OnPinch(Pinch(PinchPhase.Idle), 12f);

            Assert.AreEqual(PinchRejection.TooClose, tally.LastRejection);
            Assert.AreEqual(10f, tally.RejectedAt);
            Assert.That(StatusText.Format(View(tally, 14f)), Does.Contain("TooClose 4 s ago"));
        }

        [Test]
        public void Grab_state_follows_the_hand_that_holds_a_card()
        {
            var tally = new StatusTally();
            tally.OnGrab(HandSide.Left, Grab(GrabPhase.Holding, GrabEvent.Grabbed, 5));
            tally.OnGrab(HandSide.Right, Grab(GrabPhase.Idle, GrabEvent.None, GrabTracker.NoCard));
            Assert.That(StatusText.Format(View(tally)), Does.Contain("grab     Holding card 5, last Grabbed"));

            tally.OnGrab(HandSide.Right, Grab(GrabPhase.Holding, GrabEvent.Grabbed, 6));
            tally.OnGrab(HandSide.Left, Grab(GrabPhase.Holding, GrabEvent.None, 5));
            Assert.That(StatusText.Format(View(tally)), Does.Contain("grab     Holding card 6, last Grabbed"));

            tally.OnGrab(HandSide.Right, Grab(GrabPhase.Idle, GrabEvent.Pocketed, 6));
            tally.OnGrab(HandSide.Left, Grab(GrabPhase.Holding, GrabEvent.None, 5));
            Assert.That(StatusText.Format(View(tally)), Does.Contain("grab     Holding card 5, last Pocketed"));
        }

        [Test]
        public void Grab_state_names_the_held_card_and_the_last_event()
        {
            var tally = new StatusTally();
            tally.OnGrab(HandSide.Right, Grab(GrabPhase.Holding, GrabEvent.Grabbed, 3));

            var held = StatusText.Format(View(tally));
            tally.OnGrab(HandSide.Right, Grab(GrabPhase.Idle, GrabEvent.Pocketed, 3));
            var pocketed = StatusText.Format(View(tally));

            Assert.That(held, Does.Contain("grab     Holding card 3, last Grabbed"));
            Assert.That(pocketed, Does.Contain("grab     Idle, last Pocketed"));
        }

        [Test]
        public void A_fresh_panel_says_nothing_has_happened_yet()
        {
            var text = StatusText.Format(View(new StatusTally()));

            Assert.That(text, Does.Contain("pinch    Idle, no rejection"));
            Assert.That(text, Does.Contain("looks 0  taps 0  hung 0"));
            Assert.That(text, Does.Contain("scan     none yet"));
            Assert.That(text, Does.Contain("error    none"));
        }

        [TestCase(PairPhase.Confirming, "confirming the phone")]
        [TestCase(PairPhase.Requesting, "requesting a code")]
        [TestCase(PairPhase.Showing, "code on screen")]
        [TestCase(PairPhase.Connected, "phone connected")]
        [TestCase(PairPhase.Paired, "paired")]
        public void Pairing_reads_as_one_phrase(PairPhase phase, string expected)
        {
            var view = View(new StatusTally());
            view.Pairing = phase;

            Assert.That(StatusText.Format(view), Does.Contain("phone    " + expected));
        }

        [Test]
        public void Pairing_is_off_when_the_build_has_no_backend()
        {
            var view = View(new StatusTally());
            view.HasPairing = false;

            Assert.That(StatusText.Format(view), Does.Contain("phone    off, no backend"));
        }

        [Test]
        public void The_backend_line_gives_the_host_the_answer_and_its_age()
        {
            var up = View(new StatusTally());
            var down = up;
            down.Backend = new BackendHealth(BackendReach.Down, "timeout", 50f);
            var unknown = up;
            unknown.Backend = default;

            Assert.That(StatusText.Format(up), Does.Contain("backend  192.168.0.7:8000 up, 85 ms, 12 s ago"));
            Assert.That(StatusText.Format(down), Does.Contain("backend  192.168.0.7:8000 DOWN, timeout, 50 s ago"));
            Assert.That(StatusText.Format(unknown), Does.Contain("backend  192.168.0.7:8000, checking"));
        }

        [Test]
        public void The_error_line_shows_the_first_line_cut_to_fit()
        {
            var view = View(new StatusTally());
            view.LastError = "NullReferenceException: " + new string('x', 80) + "\nat Edith.Look.LookController.Hang";

            var lines = StatusText.Format(view).Split('\n');
            var line = lines[lines.Length - 1];

            Assert.That(line, Does.StartWith("error    NullReferenceException: xxx"));
            Assert.That(line, Does.EndWith("..."));
            Assert.That(line, Does.Not.Contain("LookController"));
            Assert.That(line.Length, Is.LessThanOrEqualTo("error    ".Length + 52));
        }

        [Test]
        public void A_short_error_is_shown_whole()
        {
            Assert.AreEqual("boom", StatusText.Brief("  boom \r\nstack", 52));
        }
    }
}
