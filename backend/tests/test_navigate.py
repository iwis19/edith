import uuid
from datetime import UTC, datetime

import httpx
import pytest
from pydantic import SecretStr, ValidationError

from edith_backend.app import create_app
from edith_backend.location import LocationFix, Locations
from edith_backend.navigate import (
    TARGET_LIFE_S, HttpPlaceFinder, NavigateRequest, Navigations, Place, SearchFailed, arrived, bearing_deg,
    compass_word, matches_name, nominatim_places, overpass_places, overpass_query,
)
from edith_backend.pairing import Pairings

from .conftest import PROFILE, client_for
from .test_location import Clock, FakeResolver

HEADERS = {"X-Edith-Token": "secret", "X-Edith-Profile": PROFILE}
# Western University's Middlesex College, and a café about 300 m north-east of it.
HERE = (43.0096, -81.2737)
CAFE = Place(name="Tim Hortons", kind="cafe", lat=43.0116, lon=-81.2712, distance_m=290)


def body(lat=HERE[0], lon=HERE[1], **extra) -> dict:
    return {"lat": lat, "lon": lon, "accuracy_m": 8.0, "taken_at": "2026-11-21T10:00:00Z", **extra}


class FakeFinder:
    def __init__(self, places=None, fail=False):
        self.places = [CAFE] if places is None else places
        self.fail = fail
        self.asked: list[tuple[float, float, NavigateRequest]] = []

    async def find(self, lat, lon, request):
        self.asked.append((lat, lon, request))
        if self.fail:
            raise SearchFailed
        return self.places


@pytest.mark.parametrize("to, expected", [
    ((44.0, -81.2737), 0.0), ((43.0096, -80.0), 90.0), ((42.0, -81.2737), 180.0), ((43.0096, -82.5), 270.0),
])
def test_the_bearing_runs_clockwise_from_true_north(to, expected):
    assert bearing_deg(*HERE, *to) == pytest.approx(expected, abs=0.6)


@pytest.mark.parametrize("bearing, word", [(0, "north"), (22, "north"), (23, "north-east"), (90, "east"),
                                           (200, "south"), (315, "north-west"), (359, "north")])
def test_the_compass_word_names_the_nearest_of_eight_points(bearing, word):
    assert compass_word(bearing) == word


def test_arrival_allows_for_a_poor_fix_but_only_up_to_a_cap():
    assert arrived(19, 5) and not arrived(25, 5)
    assert arrived(40, 45)
    assert not arrived(60, 400)


def test_the_overpass_query_searches_every_filter_of_the_category_around_the_point():
    query = overpass_query(43.0096, -81.2737, 1500, "subway")
    assert query.count("(around:1500,43.0096,-81.2737)") == 3
    assert '["railway"="subway_entrance"]' in query


def test_an_unnamed_subway_entrance_takes_the_nearest_station_name_and_places_sort_nearest_first():
    reply = {"elements": [
        {"type": "way", "center": {"lat": 43.6700, "lon": -79.3900},
         "tags": {"name": "Bloor-Yonge", "railway": "station", "station": "subway"}},
        {"type": "node", "lat": 43.6702, "lon": -79.3896, "tags": {"railway": "subway_entrance"}},
        {"type": "node", "lat": 43.6800, "lon": -79.3900, "tags": {"railway": "subway_entrance"}},
        {"type": "node", "lat": 43.6703, "lon": -79.3897, "tags": {"amenity": "bench"}},
    ]}
    places = overpass_places(reply, 43.6703, -79.3896)
    assert [p.name for p in places] == ["Bloor-Yonge entrance", "Bloor-Yonge", "Subway entrance"]
    assert places[1].kind == "subway station"


def test_nominatim_results_become_places_nearest_first_and_bad_rows_are_skipped():
    reply = [{"lat": "43.02", "lon": "-81.27", "name": "Far Cafe", "type": "cafe"},
             {"lat": "x", "lon": "-81.27", "name": "Broken"},
             {"lat": "43.0100", "lon": "-81.2740", "name": "", "display_name": "Near Cafe, Richmond Street"}]
    assert [p.name for p in nominatim_places(reply, *HERE)] == ["Near Cafe", "Far Cafe"]


@pytest.mark.parametrize("name, ok", [("tim hortons", True), ("Tim Horton's", True), ("Tims", False), ("tim", True)])
def test_a_name_matches_when_every_asked_word_is_in_the_place_name(name, ok):
    assert matches_name(CAFE, name) is ok


async def test_the_finder_filters_a_category_by_name_and_falls_back_to_nominatim():
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        if "overpass" in request.url.host:
            return httpx.Response(200, json={"elements": [
                {"type": "node", "lat": 43.0100, "lon": -81.2740, "tags": {"name": "Starbucks", "amenity": "cafe"}}]})
        return httpx.Response(200, json=[{"lat": "43.0116", "lon": "-81.2712", "name": "Tim Hortons", "type": "cafe"}])

    async def no_sleep(_):
        pass

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)), sleep=no_sleep)
    found = await finder.find(43.00961234, -81.27371234, NavigateRequest(category="cafe", name="Tim Hortons"))
    assert [p.name for p in found] == ["Tim Hortons"]
    # Coordinates leave the backend rounded, both radii were tried before the name search.
    overpass = [r for r in seen if "overpass" in r.url.host]
    assert len(overpass) == 2 and b"43.0096" in overpass[0].content and b"43.00961234" not in overpass[0].content
    assert seen[-1].url.params["q"] == "Tim Hortons" and seen[-1].url.params["bounded"] == "1"


async def test_the_finder_reports_a_failure_only_when_no_service_answered():
    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(lambda r: httpx.Response(503))))
    with pytest.raises(SearchFailed):
        await finder.find(*HERE, NavigateRequest(category="cafe"))
    empty = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(
        lambda r: httpx.Response(200, json={"elements": []}))))
    assert await empty.find(*HERE, NavigateRequest(category="cafe")) == []


def test_a_target_is_forgotten_after_two_hours():
    clock = Clock()
    navigations = Navigations(clock)
    navigations.set(uuid.UUID(PROFILE), CAFE)
    clock.now += TARGET_LIFE_S - 1
    assert navigations.active(uuid.UUID(PROFILE))
    clock.now += 2
    assert navigations.target(uuid.UUID(PROFILE)) is None


@pytest.mark.parametrize("bad", [{"heading_deg": 361}, {"heading_deg": -1}, {"speed_mps": -1}, {"course_deg": 400}])
def test_a_wild_heading_course_or_speed_is_refused(bad):
    with pytest.raises(ValidationError):
        LocationFix(**body(**bad))


@pytest.fixture
async def nav(settings, store):
    settings.device_token = SecretStr("secret")
    clock = Clock()
    locations, finder = Locations(FakeResolver(), clock), FakeFinder()
    app = create_app(settings, store=store, locations=locations, pairings=Pairings(clock),
                     navigations=Navigations(clock), finder=finder)
    async with client_for(app) as c:
        c.locations, c.clock, c.finder = locations, clock, finder
        code = (await c.post("/pair", headers=HEADERS)).json()["code"]
        c.phone = {"X-Edith-Phone": (await c.post("/pair/claim", json={"code": code})).json()["phone_token"]}
        yield c
        await locations.settle()


async def test_navigating_needs_a_recent_position(nav):
    refused = await nav.post("/navigate", json={"category": "cafe"}, headers=HEADERS)
    assert refused.status_code == 409 and refused.json()["error"]["code"] == "no_location"
    assert nav.finder.asked == []


async def test_navigating_needs_a_category_or_a_name(nav):
    await nav.post("/location", json=body(), headers=nav.phone)
    assert (await nav.post("/navigate", json={}, headers=HEADERS)).status_code == 422
    assert (await nav.post("/navigate", json={"category": "spaceport"}, headers=HEADERS)).status_code == 422


async def test_a_found_place_becomes_the_target_and_the_arrow_follows_the_phone(nav):
    await nav.post("/location", json=body(heading_deg=30.0, heading_accuracy_deg=12.0), headers=nav.phone)
    found = await nav.post("/navigate", json={"category": "cafe", "name": "Tim Hortons"}, headers=HEADERS)
    assert found.status_code == 200
    target = found.json()["target"]
    assert target["active"] and target["name"] == "Tim Hortons" and target["compass"] == "north-east"
    assert 200 < target["distance_m"] < 400 and 30 < target["bearing_deg"] < 60
    assert target["heading_deg"] == 30.0 and target["heading_accuracy_deg"] == 12.0
    assert "lat" not in target and "course_deg" not in target and found.json()["others"] == []

    # The phone hears that navigation is on, so it sends more often.
    assert (await nav.post("/location", json=body(), headers=nav.phone)).json()["navigating"] is True

    nav.clock.now += 5
    await nav.post("/location", json=body(lat=CAFE.lat - 0.0001, lon=CAFE.lon), headers=nav.phone)
    nav.clock.now += 1.5
    near = (await nav.get("/navigate", headers=HEADERS)).json()
    assert near["arrived"] is True and near["fix_age_s"] == pytest.approx(1.5)
    assert near["bearing_deg"] == pytest.approx(0, abs=1)

    assert (await nav.delete("/navigate", headers=HEADERS)).json() == {"stopped": True}
    assert (await nav.get("/navigate", headers=HEADERS)).json()["active"] is False
    assert (await nav.post("/location", json=body(), headers=nav.phone)).json()["navigating"] is False


async def test_nothing_found_and_a_failed_search_are_told_apart(nav):
    await nav.post("/location", json=body(), headers=nav.phone)
    nav.finder.places = []
    missing = await nav.post("/navigate", json={"category": "subway"}, headers=HEADERS)
    assert missing.status_code == 404 and missing.json()["error"]["code"] == "no_place"
    nav.finder.fail = True
    failed = await nav.post("/navigate", json={"category": "subway"}, headers=HEADERS)
    assert failed.status_code == 502
    # Missing fields are left out, not null.
    assert (await nav.get("/navigate", headers=HEADERS)).json() == {"active": False, "arrived": False}


async def test_the_other_matches_are_offered_without_coordinates(nav):
    await nav.post("/location", json=body(), headers=nav.phone)
    second = Place(name="Second Cup", kind="cafe", lat=43.012, lon=-81.27, distance_m=420)
    nav.finder.places = [CAFE, second]
    reply = (await nav.post("/navigate", json={"category": "cafe"}, headers=HEADERS)).json()
    assert reply["others"] == [{"name": "Second Cup", "kind": "cafe", "distance_m": 420}]


async def test_navigation_needs_the_device_token(nav):
    for call in (nav.get("/navigate"), nav.delete("/navigate"), nav.post("/navigate", json={"category": "cafe"})):
        assert (await call).status_code == 401
