using System;
using Edith.Ask;
using NUnit.Framework;

namespace Edith.Tests
{
    public class LiveMessageTests
    {
        [Test]
        public void Setup_complete_and_go_away_are_found_by_key()
        {
            Assert.IsTrue(LiveMessage.Parse("{\"setupComplete\":{}}").SetupComplete);
            Assert.IsTrue(LiveMessage.Parse("{ \"setupComplete\" : { } }").SetupComplete);
            Assert.IsTrue(LiveMessage.Parse("{\"goAway\":{\"timeLeft\":\"10s\"}}").GoAway);
            var spoken = LiveMessage.Parse("{\"serverContent\":{\"outputTranscription\":{\"text\":\"say \\\"setupComplete\\\": now\"}}}");
            Assert.IsFalse(spoken.SetupComplete, "the word inside a transcript is not the key");
        }

        [Test]
        public void Audio_and_both_transcripts_are_read_from_one_message()
        {
            var pcm = new byte[] { 1, 0, 255, 127 };
            var json = "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"" +
                       Convert.ToBase64String(pcm) + "\"}}]},\"outputTranscription\":{\"text\":\" Those ear\"}," +
                       "\"inputTranscription\":{\"text\":\"what is this\"}}}";
            var message = LiveMessage.Parse(json);
            Assert.AreEqual(1, message.Audio.Count);
            CollectionAssert.AreEqual(pcm, message.Audio[0]);
            Assert.AreEqual(" Those ear", message.OutputText);
            Assert.AreEqual("what is this", message.InputText);
            Assert.IsFalse(message.TurnComplete);
            Assert.IsFalse(message.SetupComplete);
        }

        [Test]
        public void The_end_of_a_turn_carries_its_status_and_usage()
        {
            var message = LiveMessage.Parse(
                "{\"serverContent\":{\"turnComplete\":true,\"interactionStatus\":\"IDLE\"}," +
                "\"usageMetadata\":{\"promptTokenCount\":812,\"responseTokenCount\":95,\"totalTokenCount\":907}}");
            Assert.IsTrue(message.TurnComplete);
            Assert.IsFalse(message.Interrupted);
            Assert.AreEqual(0, message.Audio.Count);
        }

        [Test]
        public void Interrupted_and_generation_complete_messages_carry_no_audio()
        {
            Assert.IsTrue(LiveMessage.Parse("{\"serverContent\":{\"interrupted\":true}}").Interrupted);
            var done = LiveMessage.Parse("{\"serverContent\":{\"generationComplete\":true}}");
            Assert.IsFalse(done.TurnComplete);
            Assert.AreEqual(0, done.Audio.Count);
        }

        [Test]
        public void Garbage_text_parts_and_bad_base64_are_ignored()
        {
            Assert.AreEqual(0, LiveMessage.Parse("not json").Audio.Count);
            Assert.AreEqual(0, LiveMessage.Parse("").Audio.Count);
            Assert.AreEqual(0, LiveMessage.Parse(null).Audio.Count);
            Assert.AreEqual(0, LiveMessage.Parse("{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"text\":\"hi\"}]}}}").Audio.Count);
            Assert.AreEqual(0, LiveMessage.Parse(
                "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm\",\"data\":\"%%\"}}]}}}").Audio.Count);
            Assert.AreEqual(0, LiveMessage.Parse(
                "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AAE=\"}}]}}}").Audio.Count);
        }

        [Test]
        public void Microphone_audio_is_a_realtime_audio_blob_of_the_bytes_given()
        {
            var json = LiveRequest.Audio(new byte[] { 0, 1, 9, 9 }, 2, 16000);
            Assert.AreEqual("{\"realtimeInput\":{\"audio\":{\"data\":\"AAE=\",\"mimeType\":\"audio/pcm;rate=16000\"}}}", json);
        }

        [Test]
        public void A_photo_is_a_realtime_video_frame()
        {
            var json = LiveRequest.Image(new byte[] { 0xFF, 0xD8, 0xFF });
            Assert.AreEqual("{\"realtimeInput\":{\"video\":{\"data\":\"/9j/\",\"mimeType\":\"image/jpeg\"}}}", json);
        }
    }

    public class LiveOutboxTests
    {
        [Test]
        public void Messages_go_out_in_order_and_a_full_outbox_drops_the_oldest_audio()
        {
            var outbox = new LiveOutbox(2);
            float[] first = { 1f }, second = { 2f }, third = { 3f };
            outbox.AddText("photo");
            Assert.IsNull(outbox.AddAudio(first));
            Assert.IsNull(outbox.AddAudio(second));
            Assert.AreSame(first, outbox.AddAudio(third), "the oldest chunk comes back to be reused");
            outbox.AddText("end");

            var sent = new System.Collections.Generic.List<object>();
            while (outbox.TryTake(out var chunk, out var json)) sent.Add((object)chunk ?? json);
            CollectionAssert.AreEqual(new object[] { "photo", second, third, "end" }, sent);
        }
    }

    public class AskGrantTests
    {
        [Test]
        public void A_setup_object_is_wrapped_into_the_setup_message()
        {
            var json = "{\"url\":\"wss://example.test/ws?access_token=abc\",\"model\":\"models/live\"," +
                       "\"setup\":{\"model\":\"models/live\",\"generationConfig\":{\"responseModalities\":[\"AUDIO\"]}}}";
            Assert.IsTrue(AskGrant.TryParse(json, out var grant));
            Assert.AreEqual("wss://example.test/ws?access_token=abc", grant.Url);
            Assert.AreEqual("models/live", grant.Model);
            Assert.AreEqual("{\"setup\":{\"model\":\"models/live\",\"generationConfig\":{\"responseModalities\":[\"AUDIO\"]}}}", grant.SetupFrame);
        }

        [Test]
        public void A_whole_setup_message_is_sent_as_it_is()
        {
            var json = "{\"setup\": {\"setup\": {\"model\": \"m\", \"systemInstruction\": {\"parts\": [{\"text\": \"a } in text\"}]}}}, \"url\": \"wss://x\"}";
            Assert.IsTrue(AskGrant.TryParse(json, out var grant));
            Assert.AreEqual("{\"setup\": {\"model\": \"m\", \"systemInstruction\": {\"parts\": [{\"text\": \"a } in text\"}]}}}", grant.SetupFrame);
            Assert.AreEqual("", grant.Model);
        }

        [Test]
        public void A_setup_encoded_as_a_string_is_decoded()
        {
            var json = "{\"url\":\"wss://x\",\"setup\":\"{\\\"model\\\":\\\"m\\\"}\",\"model\":\"m\"}";
            Assert.IsTrue(AskGrant.TryParse(json, out var grant));
            Assert.AreEqual("{\"setup\":{\"model\":\"m\"}}", grant.SetupFrame);
        }

        [TestCase("{\"setup\":{\"model\":\"m\"}}")]
        [TestCase("{\"url\":\"wss://x\"}")]
        [TestCase("{\"url\":\"wss://x\",\"setup\":null}")]
        [TestCase("{\"url\":\"wss://x\",\"setup\":{\"model\":\"m\"")]
        [TestCase("{\"url\":\"wss://x\",\"setup\":\"not json\"}")]
        [TestCase("<html>502</html>")]
        [TestCase("")]
        public void A_grant_without_a_url_or_a_setup_object_is_refused(string json)
        {
            Assert.IsFalse(AskGrant.TryParse(json, out _));
        }
    }
}
