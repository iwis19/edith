import asyncio
import uuid
from datetime import UTC, datetime

import httpx
import pytest
from pydantic import SecretStr, ValidationError

from edith_backend.app import create_app
from edith_backend.location import LocationFix, Locations
from edith_backend.navigate import (
    TARGET_LIFE_S, HttpPlaceFinder, NavigateRequest, Navigations, Place, SearchFailed, arrived, bearing_deg,
    compass_word, matches_name, nominatim_places, overpass_places, overpass_query, status,
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


async def no_sleep(_):
    pass


async def test_a_named_place_beyond_a_short_walk_comes_from_the_name_search_without_the_wide_search():
    seen: list[httpx.Request] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        if "overpass" in request.url.host:
            if b"around%3A3000" in request.content:
                await asyncio.sleep(5)
            return httpx.Response(200, json={"elements": []})
        return httpx.Response(200, json=[{"lat": "43.0116", "lon": "-81.2712", "name": "McDonald's", "type": "fast_food"}])

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)), sleep=no_sleep)
    started = asyncio.get_running_loop().time()
    found = await finder.find(43.00961234, -81.27371234, NavigateRequest(category="fast_food", name="McDonald's"))
    assert [p.name for p in found] == ["McDonald's"]
    assert asyncio.get_running_loop().time() - started < 1
    by_name = [r for r in seen if "nominatim" in r.url.host]
    assert len(by_name) == 1 and by_name[0].url.params["q"] == "McDonald's" and by_name[0].url.params["bounded"] == "1"


async def test_the_kind_search_finds_a_name_the_name_search_missed_and_coordinates_leave_rounded():
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        if "overpass" in request.url.host:
            return httpx.Response(200, json={"elements": [
                {"type": "node", "lat": 43.0100, "lon": -81.2740, "tags": {"name": "Starbucks", "amenity": "cafe"}},
                {"type": "node", "lat": 43.0110, "lon": -81.2750, "tags": {"name": "Tim Hortons", "amenity": "cafe"}}]})
        return httpx.Response(200, json=[])

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)), sleep=no_sleep)
    found = await finder.find(43.00961234, -81.27371234, NavigateRequest(category="cafe", name="Tim Hortons"))
    assert [p.name for p in found] == ["Tim Hortons"] and found[0].kind == "cafe"
    overpass = [r for r in seen if "overpass" in r.url.host]
    assert b"43.0096" in overpass[0].content and b"43.00961234" not in overpass[0].content


async def test_a_name_search_answer_without_the_whole_name_is_kept_for_when_the_map_finds_nothing():
    def handler(request: httpx.Request) -> httpx.Response:
        if "overpass" in request.url.host:
            return httpx.Response(200, json={"elements": []})
        return httpx.Response(200, json=[{"lat": "43.0116", "lon": "-81.2717", "name": "Huron College", "type": "bus_stop"},
                                         {"lat": "43.0116", "lon": "-81.2712", "name": "Schmeichel Building",
                                          "type": "university"}])

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)), sleep=no_sleep)
    found = await finder.find(*HERE, NavigateRequest(category="university",
                                                     name="Ronald D. Schmeichel Building for Entrepreneurship"))
    assert [p.name for p in found] == ["Schmeichel Building"]


async def test_a_wide_search_that_would_overrun_the_limit_ends_in_nothing_found_not_a_failure(monkeypatch):
    monkeypatch.setattr("edith_backend.navigate.NAVIGATE_TIMEOUT_S", 1.0)
    monkeypatch.setattr("edith_backend.navigate.WIDE_MIN_S", 0.1)
    queries: list[bytes] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        queries.append(request.content)
        if b"around%3A3000" in request.content or b"around:3000" in request.content:
            await asyncio.sleep(5)
        return httpx.Response(200, json={"elements": []})

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)))
    started = asyncio.get_running_loop().time()
    assert await finder.find(*HERE, NavigateRequest(category="hospital")) == []
    assert asyncio.get_running_loop().time() - started < 1.2
    assert len(queries) >= 2


async def test_the_same_search_nearby_is_remembered_with_distances_from_the_new_fix():
    calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(200, json={"elements": [
            {"type": "node", "lat": 43.0100, "lon": -81.2740, "tags": {"name": "Weldon Library", "amenity": "library"}}]})

    clock = Clock()
    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)), clock=clock)
    first = await finder.find(43.0096, -81.2737, NavigateRequest(category="library"))
    asked = calls
    again = await finder.find(43.0098, -81.2738, NavigateRequest(category="library"))
    assert calls == asked and [p.name for p in again] == ["Weldon Library"]
    assert again[0].distance_m < first[0].distance_m
    clock.now += 31 * 60
    await finder.find(43.0098, -81.2738, NavigateRequest(category="library"))
    assert calls > asked


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


async def test_a_quiet_phone_still_lets_a_search_start_from_its_last_position(nav):
    await nav.post("/location", json=body(), headers=nav.phone)
    nav.clock.now += 10 * 60
    found = await nav.post("/navigate", json={"category": "cafe"}, headers=HEADERS)
    assert found.status_code == 200 and nav.finder.asked
    nav.clock.now += 6 * 60
    refused = await nav.post("/navigate", json={"category": "cafe"}, headers=HEADERS)
    assert refused.status_code == 409 and refused.json()["error"]["code"] == "no_location"


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
    idle = (await nav.get("/navigate", headers=HEADERS)).json()
    assert idle["active"] is False and "name" not in idle and "distance_m" not in idle and "heading_deg" not in idle


async def test_with_no_target_the_newest_compass_reading_still_goes_to_the_headset(nav):
    assert (await nav.get("/navigate", headers=HEADERS)).json() == {"active": False, "arrived": False}
    await nav.post("/location", json=body(heading_deg=30.0, heading_accuracy_deg=12.0), headers=nav.phone)
    idle = (await nav.get("/navigate", headers=HEADERS)).json()
    assert idle["active"] is False and "name" not in idle and "bearing_deg" not in idle
    assert idle["heading_deg"] == 30.0 and idle["heading_accuracy_deg"] == 12.0 and idle["fix_age_s"] >= 0


async def test_the_other_matches_are_offered_without_coordinates(nav):
    await nav.post("/location", json=body(), headers=nav.phone)
    second = Place(name="Second Cup", kind="cafe", lat=43.012, lon=-81.27, distance_m=420)
    nav.finder.places = [CAFE, second]
    reply = (await nav.post("/navigate", json={"category": "cafe"}, headers=HEADERS)).json()
    assert reply["others"] == [{"name": "Second Cup", "kind": "cafe", "distance_m": 420}]


async def test_navigation_needs_the_device_token(nav):
    for call in (nav.get("/navigate"), nav.delete("/navigate"), nav.post("/navigate", json={"category": "cafe"})):
        assert (await call).status_code == 401


async def test_a_slow_overpass_mirror_is_overtaken_by_the_next_one(monkeypatch):
    monkeypatch.setattr("edith_backend.navigate.OVERPASS_HEDGE_S", 0.05)
    hosts: list[str] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        hosts.append(request.url.host)
        if request.url.host == "overpass-api.de":
            await asyncio.sleep(5)
        return httpx.Response(200, json={"elements": [
            {"type": "node", "lat": 43.0100, "lon": -81.2740, "tags": {"name": "Weldon Library", "amenity": "library"}}]})

    finder = HttpPlaceFinder(httpx.AsyncClient(transport=httpx.MockTransport(handler)))
    started = asyncio.get_running_loop().time()
    found = await finder.find(43.0096, -81.2737, NavigateRequest(category="library"))
    assert [p.name for p in found] == ["Weldon Library"]
    assert asyncio.get_running_loop().time() - started < 1
    assert hosts[:2] == ["overpass-api.de", "overpass.openstreetmap.fr"]


def test_a_place_carries_its_street_address_from_the_map_tags():
    places = overpass_places({"elements": [
        {"type": "node", "lat": 43.01, "lon": -81.27,
         "tags": {"name": "Tim Hortons", "amenity": "cafe", "addr:housenumber": "1151", "addr:street": "Richmond Street"}},
        {"type": "node", "lat": 43.02, "lon": -81.27, "tags": {"name": "Starbucks", "amenity": "cafe"}}]}, *HERE)
    assert [(p.name, p.address) for p in places] == [("Tim Hortons", "1151 Richmond Street"), ("Starbucks", None)]


async def test_a_target_without_an_address_gets_one_looked_up_in_the_background():
    class Addresser:
        async def address(self, lat, lon):
            return "1151 Richmond Street"

    clock = Clock()
    navigations = Navigations(clock)
    navigations.set(uuid.UUID(PROFILE), CAFE)
    navigations.fill_address(uuid.UUID(PROFILE), Addresser())
    await navigations.settle()
    assert navigations.target(uuid.UUID(PROFILE)).address == "1151 Richmond Street"
    fix = LocationFix(lat=HERE[0], lon=HERE[1], accuracy_m=5, taken_at=datetime.now(UTC))
    assert status(navigations.target(uuid.UUID(PROFILE)), (fix, 1.0)).address == "1151 Richmond Street"
