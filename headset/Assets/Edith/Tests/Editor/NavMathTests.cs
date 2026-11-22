using Edith.Ask;
using Edith.Navigation;
using NUnit.Framework;

namespace Edith.Tests
{
    public sealed class NavMathTests
    {
        [TestCase(0f, 0f)]
        [TestCase(190f, -170f)]
        [TestCase(-190f, 170f)]
        [TestCase(540f, 180f)]
        [TestCase(-180f, 180f)]
        public void WrapKeepsDegreesInOneTurn(float degrees, float expected) =>
            Assert.That(NavMath.Wrap(degrees), Is.EqualTo(expected).Within(1e-3f));

        [Test]
        public void ThePhoneHeadingAndTheHeadYawGiveNorth()
        {
            // Facing Unity yaw 100 while the phone reads 30 degrees east of north: north is at yaw 70, so a target due
            // east (bearing 90) lies along yaw 160.
            var north = NavMath.NorthFrom(100f, 30f);
            Assert.That(north, Is.EqualTo(70f).Within(1e-3f));
            Assert.That(NavMath.ArrowYaw(north, 90f), Is.EqualTo(160f).Within(1e-3f));
        }

        [Test]
        public void CalibrationTakesTheFirstGuessAndAveragesAcrossTheWrap()
        {
            var north = new NorthCalibration();
            Assert.That(north.Ready, Is.False);
            north.Add(170f, NavMath.CompassWeight);
            Assert.That(north.NorthYaw, Is.EqualTo(170f).Within(0.01f));
            Assert.That(north.Ready, Is.False);
            for (var i = 0; i < 6; i++) north.Add(-170f, 0.5f);
            Assert.That(north.Ready, Is.True);
            // Between 170 and -170 by the short way round, closer to the many later guesses.
            Assert.That(NavMath.Wrap(north.NorthYaw), Is.GreaterThan(170f).Or.LessThan(-160f));
            Assert.That(NavMath.Wrap(north.NorthYaw + 170f), Is.InRange(-5f, 5f));
            north.Reset();
            Assert.That(north.Strength, Is.EqualTo(0f));
        }

        [Test]
        public void APoorCompassDoesNotCount()
        {
            Assert.That(NavMath.CompassWeightFor(-1f), Is.EqualTo(NavMath.CompassWeight));
            Assert.That(NavMath.CompassWeightFor(15f), Is.EqualTo(NavMath.CompassWeight));
            Assert.That(NavMath.CompassWeightFor(60f), Is.EqualTo(0f));
            var north = new NorthCalibration();
            north.Add(45f, 0f);
            Assert.That(north.Strength, Is.EqualTo(0f));
        }

        [Test]
        public void YawHistoryFindsTheNearestRecordedMoment()
        {
            var history = new YawHistory(4);
            for (var i = 0; i < 6; i++) history.Record(i * 0.1f, i * 10f);
            Assert.That(history.TryAt(0.42f, 0.05f, out var yaw), Is.True);
            Assert.That(yaw, Is.EqualTo(40f));
            // The oldest readings were overwritten.
            Assert.That(history.TryAt(0f, 0.05f, out _), Is.False);
            Assert.That(history.TryAt(2f, 0.3f, out _), Is.False);
        }

        [TestCase(240f, "240 m · 3 min walk")]
        [TestCase(36f, "40 m · 1 min walk")]
        [TestCase(1440f, "1.4 km · 18 min walk")]
        public void DistanceReadsInMetresThenKilometres(float metres, string expected) =>
            Assert.That(NavMath.DistanceText(metres), Is.EqualTo(expected));

        [Test]
        public void AStatusKeepsMinusOneForWhatTheBackendLeftOut()
        {
            var status = NavStatus.Parse("{\"active\":true,\"name\":\"Tim Hortons\",\"distance_m\":290,\"bearing_deg\":41.5," +
                                         "\"fix_age_s\":1.2,\"heading_deg\":30.0,\"arrived\":false}");
            Assert.That(status.active && status.HasFix, Is.True);
            Assert.That(status.course_deg, Is.EqualTo(-1f));
            Assert.That(status.heading_deg, Is.EqualTo(30f));
            Assert.That(NavStatus.Parse("{\"active\":false,\"arrived\":false}").HasFix, Is.False);
        }

        [Test]
        public void TheChevronPulseRunsFromTheBackToTheTip()
        {
            // The back chevron peaks half a period in, and each chevron towards the tip peaks a lag later.
            Assert.That(NavMath.ChevronPulse(0.6f, 0, 1.2f, 0.2f, 0.25f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(NavMath.ChevronPulse(0.8f, 1, 1.2f, 0.2f, 0.25f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(NavMath.ChevronPulse(1.0f, 2, 1.2f, 0.2f, 0.25f), Is.EqualTo(1f).Within(1e-4f));
            // At its dimmest a chevron still shows.
            Assert.That(NavMath.ChevronPulse(0f, 0, 1.2f, 0.2f, 0.25f), Is.EqualTo(0.25f).Within(1e-4f));
        }

        [TestCase("left", 40f, "Turn left in 40 m")]
        [TestCase("slight_right", 8f, "Bear right now")]
        [TestCase("arrive", 1250f, "Arrive in 1.3 km")]
        [TestCase("straight", 40f, "")]
        public void AManeuverReadsAsATurnAndItsDistance(string kind, float metres, string expected) =>
            Assert.That(NavMath.ManeuverText(kind, metres), Is.EqualTo(expected));

        [Test]
        public void AStatusWithARouteArrowsAtTheWaypointAndCountsTheWalk()
        {
            var routed = NavStatus.Parse("{\"active\":true,\"distance_m\":290,\"bearing_deg\":41.5,\"fix_age_s\":1.2," +
                                         "\"waypoint_distance_m\":15,\"waypoint_bearing_deg\":9.0,\"maneuver\":\"left\"," +
                                         "\"maneuver_distance_m\":40,\"route_remaining_m\":575}");
            Assert.That(routed.HasRoute, Is.True);
            Assert.That((routed.ArrowBearing, routed.WalkMetres), Is.EqualTo((9f, 575f)));
            var straight = NavStatus.Parse("{\"active\":true,\"distance_m\":290,\"bearing_deg\":41.5,\"fix_age_s\":1.2}");
            Assert.That(straight.HasRoute, Is.False);
            Assert.That((straight.ArrowBearing, straight.WalkMetres), Is.EqualTo((41.5f, 290f)));
        }

        [Test]
        public void AToolCallIsReadWithItsArguments()
        {
            var message = LiveMessage.Parse("{\"toolCall\":{\"functionCalls\":[{\"id\":\"call-1\",\"name\":\"find_place\"," +
                                            "\"args\":{\"category\":\"cafe\",\"name\":\"Tim Hortons\"}}]}}");
            Assert.That(message.ToolCalls, Has.Count.EqualTo(1));
            var call = message.ToolCalls[0];
            Assert.That((call.Id, call.Name, call.Category, call.PlaceName),
                Is.EqualTo(("call-1", "find_place", "cafe", "Tim Hortons")));
            var stop = LiveMessage.Parse("{\"toolCall\":{\"functionCalls\":[{\"id\":\"c2\",\"name\":\"stop_navigation\"}]}}");
            Assert.That(stop.ToolCalls[0].Category, Is.EqualTo(""));
        }

        [Test]
        public void AToolResponseQuotesItsIdAndCarriesTheReply()
        {
            Assert.That(LiveRequest.ToolResponse("a\"b", "find_place", "{\"found\":false}"), Is.EqualTo(
                "{\"toolResponse\":{\"functionResponses\":[{\"id\":\"a\\\"b\",\"name\":\"find_place\",\"response\":{\"found\":false}}]}}"));
            Assert.That(NavClient.Problem("No\nplace"), Is.EqualTo("{\"found\":false,\"reason\":\"No\\nplace\"}"));
        }
    }
}
