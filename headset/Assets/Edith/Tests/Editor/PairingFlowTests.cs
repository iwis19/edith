using Edith.Pairing;
using NUnit.Framework;

namespace Edith.Tests
{
    public class PairingFlowTests
    {
        static PairingFlow ShowingCode(out float now)
        {
            var flow = new PairingFlow(false, 0f);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(0f));
            flow.OnCode("4821", "https://edith.example/phone", 0.5f);
            now = 0.5f;
            return flow;
        }

        // A remembered pairing whose launch check found the phone position.
        static PairingFlow PairedAt(float now)
        {
            var flow = new PairingFlow(true, now);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(now));
            flow.OnLocation(true, now);
            Assert.IsFalse(flow.Locked);
            return flow;
        }

        [Test]
        public void AnUnpairedHeadsetStartsLockedWithThePanelUp()
        {
            var flow = new PairingFlow(false, 0f);
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
        }

        [Test]
        public void AnUnpairedHeadsetRequestsACodeAtOnceAndShowsIt()
        {
            var flow = ShowingCode(out _);
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual("4821", flow.Code);
            Assert.AreEqual("https://edith.example/phone", flow.PhoneUrl);
        }

        [Test]
        public void AnUnpairedHeadsetNeverAsksForAKeptPositionAndOnlyACodeUnlocksIt()
        {
            var flow = new PairingFlow(false, 0f);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(0f));
            flow.OnLocation(true, 0.1f);
            Assert.IsTrue(flow.Locked);
            flow.OnCode("4821", "https://edith.example/phone", 0.2f);
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
        }

        [Test]
        public void ARememberedPairingStaysLockedWithNoPanelUntilTheLaunchCheck()
        {
            var flow = new PairingFlow(true, 0f);
            Assert.AreEqual(PairPhase.Confirming, flow.Phase);
            Assert.IsTrue(flow.Locked);
            Assert.IsFalse(flow.Visible);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(0f));
            flow.OnLocation(true, 0.3f);
            Assert.AreEqual(PairPhase.Paired, flow.Phase);
            Assert.IsFalse(flow.Locked);
            Assert.IsFalse(flow.Visible);
        }

        [Test]
        public void ARememberedPairingWithNoPhonePositionAtLaunchWaitsForThePhone()
        {
            var flow = new PairingFlow(true, 0f);
            flow.Next(0f);
            flow.OnLocation(false, 0.3f);
            Assert.AreEqual(PairPhase.Waiting, flow.Phase);
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual(PairCall.None, flow.Next(0.3f));
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(0.3f + PairingFlow.PollSeconds));
            flow.OnLocation(true, 4f);
            Assert.AreEqual(PairPhase.Paired, flow.Phase);
            Assert.IsFalse(flow.Locked);
        }

        [Test]
        public void AWaitThatRunsOutAsksForANewCode()
        {
            var flow = new PairingFlow(true, 0f);
            flow.Next(0f);
            flow.OnLocation(false, 0f);
            var now = 0f;
            while (now + PairingFlow.PollSeconds < PairingFlow.WaitSeconds)
            {
                now += PairingFlow.PollSeconds;
                Assert.AreEqual(PairCall.CheckLocation, flow.Next(now));
                flow.OnLocation(false, now);
                Assert.AreEqual(PairPhase.Waiting, flow.Phase);
            }
            Assert.AreEqual(PairCall.RequestCode, flow.Next(PairingFlow.WaitSeconds));
            Assert.IsTrue(flow.Visible);
        }

        [Test]
        public void ARememberedPairingIsTrustedWhenTheBackendCannotBeReachedAtLaunch()
        {
            var flow = new PairingFlow(true, 0f);
            flow.Next(0f);
            flow.OnError(8f);
            Assert.AreEqual(PairPhase.Paired, flow.Phase);
            Assert.IsFalse(flow.Locked);
            Assert.AreEqual(PairCall.None, flow.Next(17.9f));
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(18f));
        }

        [Test]
        public void OneCallIsInFlightAtATime()
        {
            var flow = new PairingFlow(false, 0f);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(0f));
            Assert.AreEqual(PairCall.None, flow.Next(1f));
        }

        [Test]
        public void PollsEveryThreeSeconds()
        {
            var flow = ShowingCode(out var now);
            Assert.AreEqual(PairCall.None, flow.Next(now + 2.9f));
            Assert.AreEqual(PairCall.PollStatus, flow.Next(now + 3f));
            flow.OnStatus(false, false, now + 3.1f);
            Assert.AreEqual(PairCall.None, flow.Next(now + 6f));
            Assert.AreEqual(PairCall.PollStatus, flow.Next(now + 6.1f));
        }

        [Test]
        public void TheConfirmedPairingUnlocksAndHidesThePanelAtOnce()
        {
            var flow = ShowingCode(out _);
            flow.Next(4f);
            flow.OnStatus(true, false, 4f);
            Assert.AreEqual(PairPhase.Connected, flow.Phase);
            Assert.IsFalse(flow.Locked);
            Assert.IsFalse(flow.Visible, "the panel goes as soon as the phone connects");
            Assert.AreEqual(PairCall.None, flow.Next(5.9f));
            Assert.AreEqual(PairCall.None, flow.Next(6f));
            Assert.AreEqual(PairPhase.Paired, flow.Phase);
            Assert.IsFalse(flow.Visible);
        }

        [Test]
        public void AnExpiredCodeIsReplacedAtOnce()
        {
            var flow = ShowingCode(out _);
            flow.Next(4f);
            flow.OnStatus(false, true, 4f);
            Assert.IsNull(flow.Code);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(4f));
            flow.OnCode("0937", "https://edith.example/phone", 4.2f);
            Assert.AreEqual("0937", flow.Code);
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
        }

        [Test]
        public void StaysLockedAndPollingLongAfterThreeMinutes()
        {
            var flow = ShowingCode(out var now);
            for (; now < 600f; now += PairingFlow.PollSeconds)
            {
                Assert.AreEqual(PairCall.PollStatus, flow.Next(now + PairingFlow.PollSeconds));
                flow.OnStatus(false, false, now + PairingFlow.PollSeconds);
            }
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
        }

        [Test]
        public void AReplyToTheWrongCallIsIgnored()
        {
            var flow = new PairingFlow(false, 0f);
            flow.Next(0f);
            flow.OnStatus(true, false, 0.1f);
            Assert.AreEqual(PairPhase.Requesting, flow.Phase);
            Assert.IsTrue(flow.Locked);
        }

        [Test]
        public void ErrorsRetryTenSecondsApartForeverWhileLocked()
        {
            var flow = new PairingFlow(false, 0f);
            var now = 0f;
            for (var retry = 1; retry <= 20; retry++)
            {
                Assert.AreEqual(PairCall.RequestCode, flow.Next(now));
                flow.OnError(now);
                Assert.AreEqual(PairCall.None, flow.Next(now + 9.9f), "retry " + retry + " too early");
                now += PairingFlow.RetrySeconds;
            }
            Assert.AreEqual(20, flow.Failures);
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(now));
            flow.OnCode("4821", "https://edith.example/phone", now);
            Assert.AreEqual(0, flow.Failures);
        }

        [Test]
        public void APairedHeadsetWatchesThePhonePositionEveryTenSeconds()
        {
            var flow = PairedAt(0.1f);
            Assert.IsFalse(flow.Visible);
            Assert.AreEqual(PairCall.None, flow.Next(10f));
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(10.1f));
            Assert.IsFalse(flow.Locked);
        }

        [Test]
        public void ALostPhonePositionLocksAgainAndWaitsForThePhone()
        {
            var flow = PairedAt(0f);
            var now = PairingFlow.WatchSeconds;
            for (var miss = 1; miss < PairingFlow.LostAfterMisses; miss++)
            {
                Assert.AreEqual(PairCall.CheckLocation, flow.Next(now));
                flow.OnLocation(false, now);
                Assert.IsFalse(flow.Locked, "locked after only " + miss + " misses");
                now += PairingFlow.WatchSeconds;
            }
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(now));
            flow.OnLocation(false, now);
            Assert.AreEqual(PairPhase.Waiting, flow.Phase);
            Assert.IsTrue(flow.Locked);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual(now, PairingFlow.WatchSeconds * PairingFlow.LostAfterMisses);
        }

        [Test]
        public void AFeatureFindingNoPositionLocksAtOnce()
        {
            var flow = PairedAt(0f);
            flow.PositionMissing(2f);
            Assert.AreEqual(PairPhase.Waiting, flow.Phase);
            Assert.IsTrue(flow.Locked);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(2f + PairingFlow.PollSeconds));
            flow.OnLocation(true, 6f);
            Assert.IsFalse(flow.Locked);
        }

        [Test]
        public void AFeatureReportArrivingMidCheckStillLocks()
        {
            var flow = PairedAt(0f);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(PairingFlow.WatchSeconds));
            flow.PositionMissing(PairingFlow.WatchSeconds + 0.1f);
            flow.OnLocation(false, PairingFlow.WatchSeconds + 0.2f);
            Assert.AreEqual(PairPhase.Waiting, flow.Phase);
        }

        [Test]
        public void AFeatureReportDoesNothingWhileUnpaired()
        {
            var flow = ShowingCode(out var now);
            flow.PositionMissing(now);
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
        }

        [Test]
        public void APositionComingBackClearsTheMisses()
        {
            var flow = PairedAt(0f);
            var now = PairingFlow.WatchSeconds;
            for (var i = 0; i < 10; i++)
            {
                flow.Next(now);
                flow.OnLocation(i % 2 == 1, now);
                now += PairingFlow.WatchSeconds;
            }
            Assert.IsFalse(flow.Locked);
        }

        [Test]
        public void AnUnreachableBackendNeverLocksAPairedHeadset()
        {
            var flow = PairedAt(0f);
            var now = PairingFlow.WatchSeconds;
            for (var i = 0; i < 20; i++)
            {
                Assert.AreEqual(PairCall.CheckLocation, flow.Next(now));
                flow.OnError(now);
                now += PairingFlow.RetrySeconds;
            }
            Assert.IsFalse(flow.Locked);
        }

        [Test]
        public void TextSpacesTheDigitsAndDropsTheScheme()
        {
            Assert.AreEqual("4 8 2 1", PairingText.Spaced("4821"));
            Assert.AreEqual("edith.up.railway.app/phone", PairingText.TypedAddress("https://edith.up.railway.app/phone"));
            Assert.AreEqual("192.168.1.5:8000/phone", PairingText.TypedAddress("http://192.168.1.5:8000/phone/"));
        }

        [Test]
        public void TheBackendsCodeReplyParses()
        {
            var issued = PairingClient.ParseIssued("{\"code\":\"4821\",\"expires_in_s\":600,\"phone_url\":\"https://edith.example/phone\"}");
            Assert.AreEqual("4821", issued.code);
            Assert.AreEqual(600, issued.expires_in_s);
            Assert.AreEqual("https://edith.example/phone", issued.phone_url);
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("{\"code\":\"\"}")]
        public void ACodeReplyWithNoCodeParsesAsNone(string json)
        {
            Assert.IsNull(PairingClient.ParseIssued(json));
        }

        [Test]
        public void A_shown_code_knows_when_it_runs_out()
        {
            var flow = new PairingFlow(false, 0f);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(0f));
            flow.OnCode("4821", "https://edith.example/phone", 10f, 300f);
            Assert.AreEqual(310f, flow.CodeExpiresAt, 1e-4f);
        }
    }
}
