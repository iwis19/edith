import json
import uuid
from pathlib import Path

import httpx
import pytest
from pydantic import SecretStr

from edith_backend.app import create_app
from edith_backend.location import LocationFix, Locations
from edith_backend.navigate import REROUTE_GAP_S, ROUTE_RETRY_S, Navigations, status
from edith_backend.pairing import Pairings
from edith_backend.route import (
    OFF_ROUTE_M, HttpRouter, RouteFailed, decode_polyline, distance_m, guide, locate, parse_route,
)

from .conftest import PROFILE, client_for
from .test_location import Clock, FakeResolver
from .test_navigate import CAFE, HEADERS, HERE, FakeFinder, body

# A real Valhalla pedestrian reply from Middlesex College to a café about 300 m north-east of it.
REPLY = json.loads((Path(__file__).parent / "data" / "valhalla_route.json").read_text())
ROUTE = parse_route(REPLY)
ID = uuid.UUID(PROFILE)


def fix_at(index: int, north_m: float = 0.0, east_m: float = 0.0) -> LocationFix:
    lat, lon = ROUTE.points[index]
    return LocationFix(lat=lat + north_m / 111_320, lon=lon + east_m / 81_000, accuracy_m=8.0,
                       taken_at="2026-11-21T10:00:00Z")


class FakeRouter:
    def __init__(self, fail=False):
        self.fail = fail
        self.asked: list[tuple[float, float]] = []

    async def route(self, lat, lon, to_lat, to_lon):
        self.asked.append((lat, lon))
        if self.fail:
            raise RouteFailed
        return ROUTE


def test_a_polyline_decodes_to_the_vertices_valhalla_measured():
    assert decode_polyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@", 5) == [(38.5, -120.2), (40.7, -120.95), (43.252, -126.453)]
    assert ROUTE.points[0] == (43.00959, -81.273619) and ROUTE.points[-1] == (43.011463, -81.271304)
    assert ROUTE.length_m == pytest.approx(574, abs=5)


def test_a_reply_without_a_route_is_refused():
    for bad in ({}, {"trip": {"legs": []}}, {"trip": {"legs": [{"shape": "??", "maneuvers": []}]}}):
        with pytest.raises(RouteFailed):
            parse_route(bad)


def test_the_person_is_placed_on_the_route_by_the_nearest_point():
    along, off = locate(ROUTE, *ROUTE.points[6])
    assert along == pytest.approx(ROUTE.along[6], abs=0.5) and off < 0.5
    along, off = locate(ROUTE, ROUTE.points[0][0] - 0.0002, ROUTE.points[0][1])
    assert along == pytest.approx(0, abs=0.5) and off == pytest.approx(22, abs=2)


def test_at_the_start_the_arrow_heads_for_a_vertex_beyond_the_lookahead_and_the_first_turn_is_named():
    start = ROUTE.points[0]
    guided = guide(ROUTE, *start)
    assert not guided.off_route and guided.along_m == pytest.approx(0, abs=0.5)
    assert guided.waypoint_distance_m >= 12 and (guided.waypoint_lat, guided.waypoint_lon) in ROUTE.points[1:]
    assert guided.waypoint_bearing_deg == pytest.approx(9, abs=15)
    assert guided.turn == "right" and guided.turn_distance_m == pytest.approx(ROUTE.along[6], abs=0.5)
    assert guided.remaining_m == pytest.approx(ROUTE.length_m, abs=0.5)


def test_a_turn_already_reached_is_not_named_again():
    at_right_turn = guide(ROUTE, *ROUTE.points[6])
    assert at_right_turn.turn == "left" and at_right_turn.turn_distance_m == pytest.approx(
        ROUTE.along[7] - ROUTE.along[6], abs=1)
    past_left = guide(ROUTE, *ROUTE.points[8])
    assert past_left.turn == "right" and past_left.turn_distance_m == pytest.approx(
        ROUTE.along[10] - ROUTE.along[8], abs=1)


def test_the_straight_continue_is_not_a_turn_and_the_last_one_is_the_arrival():
    assert guide(ROUTE, *ROUTE.points[20]).turn == "left"
    near_end = guide(ROUTE, *ROUTE.points[43])
    assert near_end.turn == "arrive" and near_end.turn_distance_m == pytest.approx(
        ROUTE.length_m - ROUTE.along[43], abs=1)
    assert (near_end.waypoint_lat, near_end.waypoint_lon) == ROUTE.points[-1]


def test_off_route_is_judged_at_thirty_metres():
    assert not guide(ROUTE, fix_at(10, north_m=OFF_ROUTE_M - 8).lat, fix_at(10).lon).off_route
    assert guide(ROUTE, fix_at(10, north_m=OFF_ROUTE_M + 15).lat, fix_at(10).lon).off_route


async def test_the_router_sends_rounded_walking_coordinates_and_reads_the_route():
    seen: list[httpx.Request] = []

    def handler(request):
        seen.append(request)
        return httpx.Response(200, json=REPLY)

    router = HttpRouter(httpx.AsyncClient(transport=httpx.MockTransport(handler)))
    route = await router.route(43.00961234, -81.27371234, 43.0116, -81.2712)
    sent = json.loads(seen[0].content)
    assert sent["costing"] == "pedestrian"
    assert sent["locations"][0] == {"lat": 43.0096, "lon": -81.2737} and len(route.points) == 45


async def test_the_router_reports_a_failure_for_an_error_status_or_a_non_route():
    for response in (httpx.Response(400, json={"error": "no path"}), httpx.Response(200, text="not json"),
                     httpx.Response(200, json={"trip": {}})):
        router = HttpRouter(httpx.AsyncClient(transport=httpx.MockTransport(lambda r, x=response: x)))
        with pytest.raises(RouteFailed):
            await router.route(*HERE, 43.0116, -81.2712)

    def timeout(request):
        raise httpx.ReadTimeout("slow")

    with pytest.raises(RouteFailed):
        await HttpRouter(httpx.AsyncClient(transport=httpx.MockTransport(timeout))).route(*HERE, 43.0116, -81.2712)


async def test_a_route_is_fetched_once_then_followed_and_a_far_fix_fetches_a_new_one():
    clock, router = Clock(), FakeRouter()
    navigations = Navigations(clock, router)
    navigations.set(ID, CAFE)
    assert navigations.guidance(ID, fix_at(0)) is None
    await navigations.settle()
    assert navigations.guidance(ID, fix_at(0)) and len(router.asked) == 1
    assert navigations.guidance(ID, fix_at(12)) and len(router.asked) == 1

    far = fix_at(12, north_m=200)
    assert navigations.guidance(ID, far) is None and len(router.asked) == 1
    clock.now += REROUTE_GAP_S + 1
    assert navigations.guidance(ID, far) is None
    await navigations.settle()
    assert len(router.asked) == 2


async def test_polls_do_not_hammer_the_router_while_it_fails_and_it_is_tried_again_later():
    clock, router = Clock(), FakeRouter(fail=True)
    navigations = Navigations(clock, router)
    navigations.set(ID, CAFE)
    for _ in range(5):
        assert navigations.guidance(ID, fix_at(0)) is None
        await navigations.settle()
    assert len(router.asked) == 1
    clock.now += ROUTE_RETRY_S + 1
    navigations.guidance(ID, fix_at(0))
    await navigations.settle()
    assert len(router.asked) == 2


async def test_without_a_router_the_straight_bearing_is_all_there_is():
    navigations = Navigations(Clock())
    navigations.set(ID, CAFE)
    assert navigations.guidance(ID, fix_at(0)) is None


def test_a_status_with_guidance_adds_the_waypoint_and_the_turn_beside_the_straight_line():
    fix = fix_at(0)
    reply = status(CAFE, (fix, 1.0), guide(ROUTE, fix.lat, fix.lon))
    assert reply.maneuver == "right" and reply.maneuver_distance_m == round(ROUTE.along[6])
    assert reply.waypoint_distance_m >= 12 and reply.route_remaining_m == round(ROUTE.length_m)
    assert reply.distance_m == round(distance_m(fix.lat, fix.lon, CAFE.lat, CAFE.lon))
    plain = status(CAFE, (fix, 1.0))
    assert plain.waypoint_bearing_deg is None and plain.maneuver is None


@pytest.fixture
async def routed(settings, store):
    settings.device_token = SecretStr("secret")
    clock = Clock()
    locations, router = Locations(FakeResolver(), clock), FakeRouter()
    navigations = Navigations(clock, router)
    app = create_app(settings, store=store, locations=locations, pairings=Pairings(clock), navigations=navigations,
                     finder=FakeFinder())
    async with client_for(app) as c:
        c.navigations, c.router, c.clock = navigations, router, clock
        code = (await c.post("/pair", headers=HEADERS)).json()["code"]
        c.phone = {"X-Edith-Phone": (await c.post("/pair/claim", json={"code": code})).json()["phone_token"]}
        yield c
        await locations.settle()
        await navigations.settle()


async def test_the_poll_reports_the_next_waypoint_and_turn_once_the_route_is_in(routed):
    start = ROUTE.points[0]
    await routed.post("/location", json=body(lat=start[0], lon=start[1]), headers=routed.phone)
    first = (await routed.post("/navigate", json={"category": "cafe"}, headers=HEADERS)).json()["target"]
    assert "maneuver" not in first and first["bearing_deg"] is not None
    await routed.navigations.settle()
    polled = (await routed.get("/navigate", headers=HEADERS)).json()
    assert polled["maneuver"] == "right" and polled["waypoint_distance_m"] >= 12
    assert polled["bearing_deg"] != polled["waypoint_bearing_deg"]
    assert polled["distance_m"] < polled["route_remaining_m"]


async def test_a_failing_router_leaves_the_straight_bearing_the_poll_always_had(routed):
    routed.router.fail = True
    await routed.post("/location", json=body(), headers=routed.phone)
    await routed.post("/navigate", json={"category": "cafe"}, headers=HEADERS)
    await routed.navigations.settle()
    polled = (await routed.get("/navigate", headers=HEADERS)).json()
    assert polled["active"] and polled["bearing_deg"] is not None and "waypoint_bearing_deg" not in polled
