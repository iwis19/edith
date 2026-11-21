using Edith.Ask;
using NUnit.Framework;

namespace Edith.Tests
{
    public class LiveToolCallTests
    {
        [Test]
        public void A_tool_call_gives_each_function_with_its_id_name_and_arguments()
        {
            var message = LiveMessage.Parse(
                "{\"toolCall\":{\"functionCalls\":[" +
                "{\"id\":\"call-1\",\"name\":\"start_hunt\",\"args\":{\"interests\":[\"bikes\",\"birds\"],\"extra\":3}}," +
                "{\"id\":\"call-2\",\"name\":\"stop_hunt\",\"args\":{}}]}}");

            Assert.AreEqual(2, message.ToolCalls.Count);
            Assert.AreEqual("call-1", message.ToolCalls[0].Id);
            Assert.AreEqual(LiveToolCall.StartHunt, message.ToolCalls[0].Name);
            CollectionAssert.AreEqual(new[] { "bikes", "birds" }, message.ToolCalls[0].Interests);
            Assert.AreEqual("call-2", message.ToolCalls[1].Id);
            Assert.AreEqual(LiveToolCall.StopHunt, message.ToolCalls[1].Name);
            Assert.AreEqual(0, message.ToolCalls[1].Interests.Length);
        }

        [Test]
        public void A_call_without_arguments_has_no_interests()
        {
            var message = LiveMessage.Parse("{\"toolCall\":{\"functionCalls\":[{\"id\":\"c\",\"name\":\"start_hunt\"}]}}");

            Assert.AreEqual(1, message.ToolCalls.Count);
            Assert.AreEqual(0, message.ToolCalls[0].Interests.Length);
        }

        [Test]
        public void A_cancellation_names_the_calls_to_drop()
        {
            var message = LiveMessage.Parse("{\"toolCallCancellation\":{\"ids\":[\"call-1\",\"call-2\"]}}");

            CollectionAssert.AreEqual(new[] { "call-1", "call-2" }, message.CancelledCalls);
            Assert.AreEqual(0, message.ToolCalls.Count);
        }

        [Test]
        public void An_ordinary_message_has_no_tool_calls()
        {
            var message = LiveMessage.Parse("{\"serverContent\":{\"turnComplete\":true}}");

            Assert.AreEqual(0, message.ToolCalls.Count);
            Assert.AreEqual(0, message.CancelledCalls.Count);
            Assert.IsTrue(message.TurnComplete);
        }

        [Test]
        public void A_tool_response_wraps_the_result_in_a_function_response_with_the_calls_id_and_name()
        {
            Assert.AreEqual(
                "{\"toolResponse\":{\"functionResponses\":[{\"id\":\"call-1\",\"name\":\"start_hunt\",\"response\":{\"hunt_ref\":\"abc\",\"status\":\"active\"}}]}}",
                LiveRequest.ToolResponse("call-1", "start_hunt", "{\"hunt_ref\":\"abc\",\"status\":\"active\"}"));
        }

        [Test]
        public void A_tool_response_escapes_an_id_with_quotes()
        {
            StringAssert.Contains("\"id\":\"a\\\"b\"", LiveRequest.ToolResponse("a\"b", "stop_hunt", "{}"));
        }
    }
}
