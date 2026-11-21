import asyncio
import uuid
from datetime import UTC, datetime

import pytest
from pydantic import SecretStr, ValidationError

from edith_backend.app import create_app
from edith_backend.location import (
    FRESH_S, KEEP_S, PHONE_SILENT_S, LocationFix, Locations, Resolution, Sight, Whereabouts, describe_place,
)
from edith_backend.pairing import Pairings

from .conftest import PROFILE, client_for

ANN = uuid.UUID(int=1)
BOB = uuid.UUID(int=2)

WATERLOO = Whereabouts(
    place="University of Waterloo, Waterloo, Ontario, Canada",
    sights=[Sight(name="Dana Porter Library", kind="library building", distance_m=40, tier=1),
            Sight(name="Davis Centre", kind="building", distance_m=120, tier=2)],
)


def fix(lat=43.4723, lon=-80.5449, accuracy_m=12.0) -> LocationFix:
    return LocationFix(lat=lat, lon=lon, accuracy_m=accuracy_m, taken_at=datetime(2026, 11, 21, 10, 0, tzinfo=UTC))


def body(lat=43.4723, lon=-80.5449, accuracy_m=12.0) -> dict:
    return {"lat": lat, "lon": lon, "accuracy_m": accuracy_m, "taken_at": "2026-11-21T10:00:00Z"}


class Clock:
    def __init__(self):
        self.now = 1_000_000.0

    def __call__(self):
        return self.now


class FakeResolver:
    def __init__(self, whereabouts: Whereabouts | None = WATERLOO, complete: bool = True):
        self.whereabouts, self.complete = whereabouts, complete
        self.fixes: list[LocationFix] = []

    async def resolve(self, fix):
        self.fixes.append(fix)
        await asyncio.sleep(0)
        if self.whereabouts is None:
            return None
        return Resolution(whereabouts=self.whereabouts, complete=self.complete)


@pytest.fixture
def clock():
    return Clock()


@pytest.mark.parametrize("bad", [
    {"lat": 91}, {"lat": -91}, {"lon": 181}, {"lon": -181}, {"accuracy_m": -1}, {"accuracy_m": 5001},
    {"lat": float("nan")}, {"lat": float("inf")},
])
def test_a_fix_outside_the_earth_or_with_a_wild_accuracy_is_refused(bad):
    with pytest.raises(ValidationError):
        LocationFix(**{**body(), **bad, "taken_at": "2026-11-21T10:00:00Z"})


async def test_a_fix_resolves_in_the_background_and_the_summary_follows(clock):
    locations = Locations(FakeResolver(), clock)
    first = locations.update(ANN, fix())
    assert (first.place, first.resolving) == (None, True)
    await locations.settle()
    summary = locations.summary(ANN)
    assert summary.place == WATERLOO.place
    assert (summary.sights_count, summary.nearest, summary.resolving) == (
        2, ["Dana Porter Library", "Davis Centre"], False)
    assert locations.whereabouts(ANN) == WATERLOO


async def test_a_fix_is_kept_for_six_hours_after_the_last_one_and_then_forgotten(clock):
    locations = Locations(FakeResolver(), clock)
    locations.update(ANN, fix())
    await locations.settle()
    clock.now += KEEP_S
    assert locations.has_fix(ANN)
    locations.update(ANN, fix())
    clock.now += KEEP_S
    assert locations.has_fix(ANN), "a newer fix restarts the six hours"
    clock.now += 1
    assert not locations.has_fix(ANN)
    assert locations.summary(ANN) is None
    assert locations.whereabouts(ANN) is None


async def test_a_prompt_stops_trusting_a_position_the_phone_has_stopped_updating(clock):
    locations = Locations(FakeResolver(), clock)
    locations.update(ANN, fix())
    await locations.settle()
    clock.now += FRESH_S
    assert locations.whereabouts(ANN) == WATERLOO
    clock.now += 1
    assert locations.whereabouts(ANN) is None
    assert locations.has_fix(ANN), "the pairing is still known, so the headset does not ask again"
    assert locations.whereabouts(ANN, max_age_s=KEEP_S) == WATERLOO


async def test_the_headset_sees_no_position_once_the_phone_has_been_silent_for_thirty_seconds(clock):
    locations = Locations(FakeResolver(), clock)
    locations.update(ANN, fix())
    await locations.settle()
    clock.now += PHONE_SILENT_S
    assert locations.summary(ANN).place == WATERLOO.place
    clock.now += 1
    assert locations.summary(ANN) is None
    assert locations.whereabouts(ANN) == WATERLOO, "prompts keep the place for FRESH_S"
    locations.update(ANN, fix())
    assert locations.summary(ANN) is not None, "the next re-send brings the phone back"


async def test_positions_are_kept_per_profile(clock):
    locations = Locations(FakeResolver(), clock)
    locations.update(ANN, fix())
    await locations.settle()
    assert locations.whereabouts(BOB) is None
    assert locations.summary(BOB) is None


async def test_expired_entries_are_dropped_when_another_fix_arrives(clock):
    locations = Locations(FakeResolver(), clock)
    locations.update(ANN, fix())
    clock.now += KEEP_S + 1
    locations.update(BOB, fix())
    assert list(locations._entries) == [BOB]


async def test_a_nearby_fix_does_not_resolve_again_but_a_far_one_does(clock):
    resolver = FakeResolver()
    locations = Locations(resolver, clock)
    locations.update(ANN, fix())
    await locations.settle()
    locations.update(ANN, fix(lat=43.4724))
    await locations.settle()
    assert len(resolver.fixes) == 1
    locations.update(ANN, fix(lat=43.4823))
    await locations.settle()
    assert len(resolver.fixes) == 2


async def test_an_old_resolution_is_refreshed_and_a_partial_one_is_retried_sooner(clock):
    resolver = FakeResolver()
    locations = Locations(resolver, clock)
    locations.update(ANN, fix())
    await locations.settle()
    clock.now += 601
    locations.update(ANN, fix())
    await locations.settle()
    assert len(resolver.fixes) == 2

    partial = FakeResolver(complete=False)
    locations = Locations(partial, clock)
    locations.update(ANN, fix())
    await locations.settle()
    clock.now += 30
    locations.update(ANN, fix())
    await locations.settle()
    assert len(partial.fixes) == 1
    clock.now += 31
    locations.update(ANN, fix())
    await locations.settle()
    assert len(partial.fixes) == 2


async def test_a_failed_resolve_keeps_the_fix_and_the_earlier_answer(clock):
    resolver = FakeResolver()
    locations = Locations(resolver, clock)
    locations.update(ANN, fix())
    await locations.settle()
    resolver.whereabouts = None
    clock.now += 601
    locations.update(ANN, fix())
    await locations.settle()
    assert locations.summary(ANN).place == WATERLOO.place
    assert not locations.summary(ANN).resolving


def test_the_place_description_names_the_place_and_the_nearest_sights_in_order():
    text = describe_place(WATERLOO, 5)
    assert text.startswith("This person is at University of Waterloo, Waterloo, Ontario, Canada.")
    assert text.index("Dana Porter Library (40 m, library building)") < text.index("Davis Centre (120 m, building)")
    assert "Dana Porter" not in describe_place(WATERLOO, 0)


@pytest.mark.parametrize("found, expected", [
    (None, None),
    (Whereabouts(place=None, sights=[]), None),
    (Whereabouts(place="Paris, France", sights=[]), "This person is at Paris, France. No named sights are known nearby."),
    (Whereabouts(place=None, sights=WATERLOO.sights[:1]),
     "This person's place name is not known. Named sights nearby, nearest first: Dana Porter Library (40 m, library building)."),
])
def test_the_place_description_covers_missing_parts(found, expected):
    assert describe_place(found, 5) == expected


@pytest.fixture
async def http(settings, store, clock):
    settings.device_token = SecretStr("secret")
    locations = Locations(FakeResolver(), clock)
    app = create_app(settings, store=store, locations=locations, pairings=Pairings(clock))
    async with client_for(app) as c:
        c.locations, c.clock = locations, clock
        yield c


async def paired_phone_token(http) -> str:
    headers = {"X-Edith-Token": "secret", "X-Edith-Profile": PROFILE}
    code = (await http.post("/pair", headers=headers)).json()["code"]
    return (await http.post("/pair/claim", json={"code": code})).json()["phone_token"]


async def test_a_paired_phone_posts_its_position_without_the_device_token(http):
    token = await paired_phone_token(http)
    posted = await http.post("/location", json=body(), headers={"X-Edith-Phone": token})
    assert posted.status_code == 200
    assert posted.json()["resolving"] is True
    await http.locations.settle()
    again = await http.post("/location", json=body(), headers={"X-Edith-Phone": token})
    assert again.json() == {"place": WATERLOO.place, "sights_count": 2,
                            "nearest": ["Dana Porter Library", "Davis Centre"], "resolving": False}
    assert http.locations.whereabouts(uuid.UUID(PROFILE)) == WATERLOO


@pytest.mark.parametrize("token", [None, "", "garbage", "a.b"])
async def test_a_position_without_a_valid_phone_token_is_refused_and_kept_nowhere(http, token):
    headers = {"X-Edith-Phone": token} if token is not None else {}
    response = await http.post("/location", json=body(), headers=headers | {"X-Edith-Profile": PROFILE})
    assert (response.status_code, response.json()["error"]["code"]) == (401, "bad_phone_token")
    assert not http.locations.has_fix(uuid.UUID(PROFILE))


async def test_the_device_token_alone_cannot_post_a_position(http):
    headers = {"X-Edith-Token": "secret", "X-Edith-Profile": PROFILE}
    assert (await http.post("/location", json=body(), headers=headers)).status_code == 401


async def test_a_phone_token_from_another_backend_is_refused(http, settings, store, clock):
    token = await paired_phone_token(http)
    settings.device_token = SecretStr("other-secret")
    async with client_for(create_app(settings, store=store, locations=Locations(FakeResolver(), clock))) as c:
        response = await c.post("/location", json=body(), headers={"X-Edith-Phone": token})
    assert response.status_code == 401


async def test_the_headset_reads_whether_a_position_is_kept_and_never_the_coordinates(http):
    headers = {"X-Edith-Token": "secret", "X-Edith-Profile": PROFILE}
    missing = await http.get("/location", headers=headers)
    assert (missing.status_code, missing.json()["error"]["code"]) == (404, "no_location")

    token = await paired_phone_token(http)
    await http.post("/location", json=body(lat=43.4723, lon=-80.5449), headers={"X-Edith-Phone": token})
    await http.locations.settle()
    kept = await http.get("/location", headers=headers)
    assert kept.status_code == 200
    assert kept.json()["place"] == WATERLOO.place
    assert "43.47" not in kept.text and "80.54" not in kept.text

    http.clock.now += PHONE_SILENT_S + 1
    silent = await http.get("/location", headers=headers)
    assert (silent.status_code, silent.json()["error"]["code"]) == (404, "no_location")

    other = await http.get("/location", headers={"X-Edith-Token": "secret", "X-Edith-Profile": str(BOB)})
    assert other.status_code == 404


async def test_a_position_body_over_four_kilobytes_is_refused_before_it_is_read(http):
    token = await paired_phone_token(http)
    response = await http.post("/location", content=b"x" * 5000, headers={"X-Edith-Phone": token})
    assert (response.status_code, response.json()["error"]["code"]) == (413, "body_too_large")


async def test_an_invalid_position_is_refused_with_a_valid_token(http):
    token = await paired_phone_token(http)
    response = await http.post("/location", json=body(lat=200), headers={"X-Edith-Phone": token})
    assert (response.status_code, response.json()["error"]["code"]) == (422, "invalid_request")
    assert not http.locations.has_fix(uuid.UUID(PROFILE))


async def test_the_phone_page_is_served_without_the_device_token_and_holds_no_secret(http):
    response = await http.get("/phone")
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("text/html")
    assert response.headers["cache-control"] == "no-store"
    assert "secret" not in response.text
    assert "default-src 'none'" in response.headers["content-security-policy"]
    assert '"/pair/claim"' in response.text and "X-Edith-Phone" in response.text


async def test_no_other_route_is_open_without_the_device_token(http):
    for method, path in [("post", "/look"), ("post", "/collect"), ("get", "/memories"), ("post", "/ask/session"),
                         ("post", "/pair"), ("get", "/location"), ("get", "/pair/1234"), ("post", "/phone"),
                         ("get", "/pair/claim")]:
        response = await getattr(http, method)(path)
        assert response.status_code == 401, (method, path)
