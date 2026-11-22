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
        public void NorthLocksOnceThreeCompassGuessesAgreeAndAveragesAcrossTheWrap()
        {
            var north = new NorthCalibration();
            north.AddCompass(175f);
            north.AddCompass(-178f);
            Assert.That(north.Ready, Is.False);
            north.AddCompass(-175f);
            Assert.That(north.Ready, Is.True);
            Assert.That(north.Walked, Is.False);
            Assert.That(NavMath.Wrap(north.NorthYaw - 180f), Is.InRange(-4f, 4f));
        }

        [Test]
        public void ACompassGuessThatDisagreesStartsTheRunAgain()
        {
            var north = new NorthCalibration();
            north.AddCompass(10f);
            north.AddCompass(12f);
            north.AddCompass(60f);
            north.AddCompass(62f);
            Assert.That(north.Ready, Is.False);
            north.AddCompass(61f);
            Assert.That(north.NorthYaw, Is.EqualTo(61f).Within(0.01f));
        }

        [Test]
        public void TurningTheHeadAwayFromThePhoneCannotMoveALockedNorth()
        {
            var north = new NorthCalibration();
            for (var i = 0; i < 3; i++) north.AddCompass(20f);
            // The head turns 90 degrees and then right round while the phone stays put.
            for (var i = 0; i < 40; i++) north.AddCompass(110f);
            for (var i = 0; i < 40; i++) north.AddCompass(-160f);
            Assert.That(north.NorthYaw, Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void WalkingNudgesANearNorthAndReplacesAWrongOne()
        {
            var north = new NorthCalibration();
            for (var i = 0; i < 3; i++) north.AddCompass(20f);
            north.AddCourse(30f);
            north.AddCourse(30f);
            Assert.That(north.NorthYaw, Is.EqualTo(20f).Within(0.01f));
            north.AddCourse(30f);
            Assert.That(north.NorthYaw, Is.EqualTo(23f).Within(0.01f));
            Assert.That(north.Walked, Is.True);
            for (var i = 0; i < 3; i++) north.AddCourse(140f);
            Assert.That(north.NorthYaw, Is.EqualTo(140f).Within(0.01f));
            north.Reset();
            Assert.That(north.Ready || north.Walked, Is.False);
        }

        [Test]
        public void WalkingAloneLocksNorth()
        {
            var north = new NorthCalibration();
            north.AddCourse(-50f);
            north.AddCourse(-45f);
            north.AddCourse(-40f);
            Assert.That(north.Ready && north.Walked, Is.True);
            Assert.That(north.NorthYaw, Is.EqualTo(-45f).Within(0.01f));
        }

        [Test]
        public void APoorCompassDoesNotCount()
        {
            Assert.That(NavMath.CompassUsable(-1f), Is.True);
            Assert.That(NavMath.CompassUsable(15f), Is.True);
            Assert.That(NavMath.CompassUsable(60f), Is.False);
        }

        [Test]
        public void TheHeadIsSteadyOnlyWhileItKeepsToOneDirection()
        {
            var history = new YawHistory();
            for (var i = 0; i < 20; i++) history.Record(i * 0.1f, 358f + i * 0.2f);
            for (var i = 20; i < 40; i++) history.Record(i * 0.1f, 2f + (i - 20) * 3f);
            Assert.That(history.Steady(0f, 1.9f, 10f), Is.True);
            Assert.That(history.Steady(1f, 3f, 10f), Is.False);
            Assert.That(history.Steady(10f, 12f, 10f), Is.False);
        }

        [TestCase(40f, 40f, 0f)]
        [TestCase(40f, -50f, 90f)]
        [TestCase(-170f, 10f, 180f)]
        [TestCase(10f, 40f, -30f)]
        public void TheChevronTurnsByTheWayToGoSeenFromTheHead(float arrowYaw, float headYaw, float expected) =>
            Assert.That(NavMath.ChevronTurn(arrowYaw, headYaw), Is.EqualTo(expected).Within(0.01f));

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

        [TestCase(240f, "3 min · 240 m")]
        [TestCase(36f, "1 min · 40 m")]
        [TestCase(1440f, "18 min · 1.4 km")]
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

        [TestCase("left", 40f, "Turn left in 40 m")]
        [TestCase("right", 8f, "Turn right in 10 m")]
        [TestCase("right", 14f, "Turn right in 10 m")]
        [TestCase("right", 3f, "Turn right")]
        [TestCase("slight_right", 8f, "Keep right in 10 m")]
        [TestCase("slight_right", 2f, "Keep right")]
        [TestCase("arrive", 1250f, "Arrive in 1.3 km")]
        [TestCase("straight", 40f, "")]
        public void AManeuverReadsAsATurnAndItsDistance(string kind, float metres, string expected) =>
            Assert.That(NavMath.ManeuverText(kind, metres), Is.EqualTo(expected));

        [TestCase("left", 40f, 240f, "Turn left in 40 m · 3 min")]
        [TestCase("", 0f, 1440f, "18 min · 1.4 km")]
        [TestCase("straight", 40f, 240f, "3 min · 240 m")]
        public void TheGuideLineIsTheNextTurnAndTheWalkLeft(string kind, float turnMetres, float walkMetres, string expected) =>
            Assert.That(NavMath.GuideText(kind, turnMetres, walkMetres), Is.EqualTo(expected));

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
