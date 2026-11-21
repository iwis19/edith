using Edith.Pairing;
using NUnit.Framework;

namespace Edith.Tests
{
    public class PairingFlowTests
    {
        static PairingFlow ShowingCode(out float now)
        {
            var flow = new PairingFlow(0f);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(0f));
            flow.OnLocation(false, 0.2f);
            Assert.AreEqual(PairCall.RequestCode, flow.Next(0.2f));
            flow.OnCode("4821", "https://edith.example/phone", 0.5f);
            now = 0.5f;
            return flow;
        }

        [Test]
        public void ChecksThenRequestsThenShowsTheCode()
        {
            var flow = ShowingCode(out _);
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual("4821", flow.Code);
            Assert.AreEqual("https://edith.example/phone", flow.PhoneUrl);
        }

        [Test]
        public void AKnownPhoneLocationHidesTheFlowWithoutAskingForACode()
        {
            var flow = new PairingFlow(0f);
            flow.Next(0f);
            flow.OnLocation(true, 0.1f);
            Assert.AreEqual(PairPhase.Hidden, flow.Phase);
            Assert.AreEqual(PairHideReason.HasLocation, flow.HideReason);
            Assert.AreEqual(PairCall.None, flow.Next(0.2f));
        }

        [Test]
        public void OneCallIsInFlightAtATime()
        {
            var flow = new PairingFlow(0f);
            Assert.AreEqual(PairCall.CheckLocation, flow.Next(0f));
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
        public void PairingShowsConnectedForTwoSecondsThenHides()
        {
            var flow = ShowingCode(out _);
            flow.Next(4f);
            flow.OnStatus(true, false, 4f);
            Assert.AreEqual(PairPhase.Connected, flow.Phase);
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual(PairCall.None, flow.Next(5.9f));
            Assert.IsTrue(flow.Visible);
            Assert.AreEqual(PairCall.None, flow.Next(6f));
            Assert.AreEqual(PairPhase.Hidden, flow.Phase);
            Assert.AreEqual(PairHideReason.Done, flow.HideReason);
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
        public void HidesAfterThreeMinutesUnpaired()
        {
            var flow = ShowingCode(out _);
            flow.Next(179.9f);
            flow.OnStatus(false, false, 179.9f);
            Assert.IsTrue(flow.Visible);
            flow.Next(180f);
            Assert.AreEqual(PairHideReason.TimedOut, flow.HideReason);
            Assert.IsFalse(flow.Visible);
        }

        [Test]
        public void ALateReplyAfterHidingChangesNothing()
        {
            var flow = ShowingCode(out _);
            flow.Next(3.5f);
            flow.Next(181f);
            flow.OnStatus(true, false, 181.5f);
            Assert.AreEqual(PairPhase.Hidden, flow.Phase);
            Assert.AreEqual(PairHideReason.TimedOut, flow.HideReason);
        }

        [Test]
        public void AReplyToTheWrongCallIsIgnored()
        {
            var flow = new PairingFlow(0f);
            flow.Next(0f);
            flow.OnCode("1111", "https://edith.example/phone", 0.1f);
            Assert.AreEqual(PairPhase.Checking, flow.Phase);
            Assert.IsNull(flow.Code);
        }

        [Test]
        public void ErrorsRetryTenSecondsApartThenHideAfterThree()
        {
            var flow = new PairingFlow(0f);
            flow.Next(0f);
            flow.OnLocation(false, 0f);
            var now = 0f;
            for (var retry = 1; retry <= PairingFlow.MaxRetries; retry++)
            {
                Assert.AreEqual(PairCall.RequestCode, flow.Next(now));
                flow.OnError(now);
                Assert.AreEqual(PairCall.None, flow.Next(now + 9.9f), $"retry {retry} too early");
                now += 10f;
            }
            Assert.AreEqual(PairCall.RequestCode, flow.Next(now));
            flow.OnError(now);
            Assert.AreEqual(PairPhase.Hidden, flow.Phase);
            Assert.AreEqual(PairHideReason.Errors, flow.HideReason);
        }

        [Test]
        public void ASuccessResetsTheRetryBudget()
        {
            var flow = ShowingCode(out var now);
            for (var i = 0; i < 5; i++)
            {
                now += 3f;
                Assert.AreEqual(PairCall.PollStatus, flow.Next(now));
                flow.OnError(now);
                now += 10f;
                Assert.AreEqual(PairCall.PollStatus, flow.Next(now));
                flow.OnStatus(false, false, now);
            }
            Assert.AreEqual(PairPhase.Showing, flow.Phase);
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
    }
}
