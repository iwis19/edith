using Edith.Welcome;
using NUnit.Framework;

namespace Edith.Tests
{
    public class WelcomeFlowTests
    {
        [Test]
        public void It_starts_on_the_first_step_with_the_light_voice_and_nothing_picked()
        {
            var flow = new WelcomeFlow();
            Assert.AreEqual(WelcomeStep.Interests, flow.Step);
            Assert.AreEqual(VoiceStyle.Playful, flow.Voice);
            Assert.IsEmpty(flow.Interests);
        }

        [Test]
        public void An_interest_is_picked_and_put_back_by_the_same_tap()
        {
            var flow = new WelcomeFlow();
            Assert.IsTrue(flow.Toggle("Food"));
            Assert.IsTrue(flow.Toggle("Art"));
            CollectionAssert.AreEqual(new[] { "Food", "Art" }, flow.Interests);
            Assert.IsTrue(flow.Toggle("Food"));
            CollectionAssert.AreEqual(new[] { "Art" }, flow.Interests);
            Assert.IsFalse(flow.Toggle("Knitting"), "only the twelve offered");
        }

        [Test]
        public void Next_goes_to_pairing_and_a_connected_phone_finishes_the_welcome()
        {
            var flow = new WelcomeFlow();
            flow.Next();
            Assert.AreEqual(WelcomeStep.Pair, flow.Step);
            flow.PhoneConnected();
            Assert.IsTrue(flow.Finished);
        }

        [Test]
        public void A_phone_paired_early_skips_the_pairing_step()
        {
            var flow = new WelcomeFlow();
            flow.PhoneConnected();
            Assert.AreEqual(WelcomeStep.Interests, flow.Step, "the person has not pressed Next yet");
            flow.Next();
            Assert.IsTrue(flow.Finished);
        }

        [Test]
        public void Picks_are_ignored_once_past_the_first_step()
        {
            var flow = new WelcomeFlow();
            flow.Next();
            Assert.IsFalse(flow.Toggle("Food"));
            flow.ChooseVoice(VoiceStyle.Enthusiastic);
            Assert.AreEqual(VoiceStyle.Playful, flow.Voice);
        }

        [Test]
        public void Each_voice_style_maps_to_a_distinct_gemini_voice()
        {
            Assert.AreEqual("Laomedeia", WelcomeStore.VoiceName(VoiceStyle.Enthusiastic));
            Assert.AreEqual("Vindemiatrix", WelcomeStore.VoiceName(VoiceStyle.Calm));
            Assert.AreEqual("Callirrhoe", WelcomeStore.VoiceName(VoiceStyle.Playful));
        }
    }
}
