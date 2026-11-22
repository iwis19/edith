import asyncio
import uuid
from datetime import UTC, datetime, timedelta

import pytest
from pydantic import SecretStr

from edith_backend.app import create_app
from edith_backend.location import LocationFix, Locations
from edith_backend.navigate import Navigations, Place
from edith_backend.pairing import Pairings
from edith_backend.trails import (
    CELL_DEG, MIN_AREA_M, DisabledTrailStore, cell_centre, cell_of, prefer_unwalked, unexplored_areas,
)

from .conftest import client_for
from .test_location import Clock, FakeResolver
from .test_navigate import HEADERS, HERE, FakeFinder, body

NEAR = Place(name="Near", kind="cafe", lat=HERE[0] + 0.0005, lon=HERE[1], distance_m=55)
FAR = Place(name="Far", kind="cafe", lat=HERE[0] + 0.006, lon=HERE[1], distance_m=670)


class FakeTrails(DisabledTrailStore):
    def __init__(self, walked=None):
        self.walked = walked
        self.recorded: list[tuple[uuid.UUID, uuid.UUID, LocationFix]] = []

    async def record(self, profile_id, adventure_id, fix):
        self.recorded.append((profile_id, adventure_id, fix))

    async def walked_cells(self, profile_id, lat, lon, radius_m):
        return self.walked


def test_a_point_belongs_to_one_cell_and_the_cell_centre_lies_inside_it():
    cell = cell_of(*HERE)
    assert cell_of(*cell_centre(cell)) == cell
    assert abs(cell_centre(cell)[0] - HERE[0]) <= CELL_DEG


def test_unexplored_areas_skip_walked_cells_the_standing_cell_and_anything_too_close():
    here = cell_of(*HERE)
    walked = {(here[0] + dy, here[1] + dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1)}
    areas = unexplored_areas(*HERE, 1500, walked)
    assert areas and all(MIN_AREA_M <= a.distance_m <= 1500 for a in areas)
    assert [a.distance_m for a in areas] == sorted(a.distance_m for a in areas)
    assert not {cell_of(a.lat, a.lon) for a in areas} & walked
    assert len(areas) <= 5


def test_a_fully_walked_neighbourhood_has_nothing_unexplored():
    here = cell_of(*HERE)
    walked = {(here[0] + dy, here[1] + dx) for dy in range(-12, 13) for dx in range(-12, 13)}
    assert unexplored_areas(*HERE, 1500, walked) == []


def test_a_place_in_a_never_walked_cell_moves_to_the_front():
    here = cell_of(*HERE)
    places, found = prefer_unwalked([NEAR, FAR], {cell_of(NEAR.lat, NEAR.lon)}, here)
    assert [p.name for p in places] == ["Far", "Near"] and found
    places, found = prefer_unwalked([NEAR, FAR], {cell_of(NEAR.lat, NEAR.lon), cell_of(FAR.lat, FAR.lon)}, here)
    assert [p.name for p in places] == ["Near", "Far"] and not found


@pytest.fixture
async def trailed(settings, store):
    settings.device_token = SecretStr("secret")
    clock = Clock()
    locations, finder, trails = Locations(FakeResolver(), clock), FakeFinder([NEAR, FAR]), FakeTrails(set())
    app = create_app(settings, store=store, locations=locations, pairings=Pairings(clock),
                     navigations=Navigations(clock), finder=finder, trails=trails)
    async with client_for(app) as c:
        c.trails = trails
        code = (await c.post("/pair", headers=HEADERS)).json()["code"]
        c.phone = {"X-Edith-Phone": (await c.post("/pair/claim", json={"code": code})).json()["phone_token"]}
        yield c
        await locations.settle()


async def test_fixes_are_recorded_under_one_adventure_only_while_navigating(trailed):
    await trailed.post("/location", json=body(), headers=trailed.phone)
    assert trailed.trails.recorded == []
    await trailed.post("/navigate", json={"category": "cafe"}, headers=HEADERS)
    for minute in (1, 2):
        stamp = (datetime(2026, 11, 21, 10, tzinfo=UTC) + timedelta(minutes=minute)).isoformat()
        await trailed.post("/location", json=body(taken_at=stamp), headers=trailed.phone)
    await asyncio.sleep(0)
    assert len(trailed.trails.recorded) == 2
    assert len({adventure for _, adventure, _ in trailed.trails.recorded}) == 1


async def test_the_unexplored_endpoint_lists_nearby_areas_and_needs_a_position_and_trails(trailed):
    assert (await trailed.get("/trails/unexplored", headers=HEADERS)).status_code == 409
    await trailed.post("/location", json=body(), headers=trailed.phone)
    reply = (await trailed.get("/trails/unexplored", headers=HEADERS)).json()
    assert reply["walked_nearby"] == 0 and 0 < len(reply["areas"]) <= 5
    assert set(reply["areas"][0]) == {"lat", "lon", "distance_m", "bearing_deg", "compass"}
    trailed.trails.walked = None
    assert (await trailed.get("/trails/unexplored", headers=HEADERS)).status_code == 503


async def test_asking_for_a_new_area_prefers_a_place_in_a_cell_never_walked(trailed):
    await trailed.post("/location", json=body(), headers=trailed.phone)
    trailed.trails.walked = {cell_of(NEAR.lat, NEAR.lon)}
    reply = (await trailed.post("/navigate", json={"category": "cafe", "new_area": True}, headers=HEADERS)).json()
    assert reply["target"]["name"] == "Far" and reply["new_area"] is True
    plain = (await trailed.post("/navigate", json={"category": "cafe"}, headers=HEADERS)).json()
    assert plain["target"]["name"] == "Near" and "new_area" not in plain
    trailed.trails.walked = None
    unknown = (await trailed.post("/navigate", json={"category": "cafe", "new_area": True}, headers=HEADERS)).json()
    assert unknown["target"]["name"] == "Near" and "new_area" not in unknown
