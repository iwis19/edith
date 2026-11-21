using Edith.Bounty;
using NUnit.Framework;

namespace Edith.Tests
{
    public class HuntClientTests
    {
        [Test]
        public void Starting_with_interests_sends_them_and_without_sends_an_empty_object()
        {
            Assert.AreEqual("{\"interests\":[\"bikes\",\"birds\"]}", HuntClient.StartBody(new[] { "bikes", "birds" }));
            Assert.AreEqual("{}", HuntClient.StartBody(new string[0]));
            Assert.AreEqual("{}", HuntClient.StartBody(null));
        }

        [Test]
        public void An_interest_with_quotes_is_escaped()
        {
            Assert.AreEqual("{\"interests\":[\"the \\\"old\\\" city\"]}", HuntClient.StartBody(new[] { "the \"old\" city" }));
        }

        [Test]
        public void A_good_reply_is_the_hunt_and_goes_back_to_edith_as_it_came()
        {
            var reply = new HuntReply(200, HuntStateTests.Active);

            Assert.IsTrue(reply.Ok);
            Assert.IsNotNull(reply.State);
            Assert.AreEqual(HuntStateTests.Active, reply.ToolResult);
        }

        [Test]
        public void No_hunt_on_stop_goes_back_as_the_backends_error_for_edith_to_speak_about()
        {
            const string body = "{\"error\":{\"code\":\"no_hunt\",\"message\":\"There is no active hunt.\"}}";
            var reply = new HuntReply(404, body);

            Assert.IsTrue(reply.NoHunt);
            Assert.IsNull(reply.State);
            Assert.AreEqual(body, reply.ToolResult);
        }

        [Test]
        public void A_backend_that_could_not_be_reached_goes_back_as_an_error_object()
        {
            var reply = new HuntReply(0, HuntReply.ErrorBody("unreachable", "The hunt service could not be reached."));

            Assert.IsFalse(reply.Ok);
            Assert.AreEqual("{\"error\":{\"code\":\"unreachable\",\"message\":\"The hunt service could not be reached.\"}}", reply.ToolResult);
        }

        [Test]
        public void An_error_with_a_body_that_is_not_an_error_object_is_wrapped_so_edith_still_gets_an_object()
        {
            var reply = new HuntReply(502, "<html>Bad gateway</html>");

            Assert.AreEqual("{\"error\":{\"code\":\"http_502\",\"message\":\"The hunt service answered with an error.\"}}", reply.ToolResult);
        }
    }
}
