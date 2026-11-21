import asyncio
import io

import httpx
import pytest
from PIL import Image
from pydantic import SecretStr

from edith_backend.app import create_app
from edith_backend.maps import IMAGE_PX, MAX_LAT, OFF_MAP, TILE_CACHE_MAX, TILE_PX, TileMap, Window, world_pixel
from edith_backend.resolve import USER_AGENT

from .conftest import PROFILE, client_for

HEADERS = {"X-Edith-Profile": PROFILE}


def tile_colour(z: int, x: int, y: int) -> tuple[int, int, int]:
    return (x % 256, y % 256, 100 + z)


def tile_png(z: int, x: int, y: int) -> bytes:
    out = io.BytesIO()
    Image.new("RGB", (TILE_PX, TILE_PX), tile_colour(z, x, y)).save(out, "PNG")
    return out.getvalue()


class FakeTiles:
    """Serves a solid-colour tile per coordinate and records every request."""

    def __init__(self, status: int = 200, body: bytes | None = None) -> None:
        self.status, self.body = status, body
        self.requests: list[httpx.Request] = []
        self.in_flight = self.most_in_flight = 0

    async def __call__(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        self.in_flight += 1
        self.most_in_flight = max(self.most_in_flight, self.in_flight)
        await asyncio.sleep(0.005)
        self.in_flight -= 1
        z, x, y = (int(part) for part in request.url.path.removesuffix(".png").strip("/").split("/"))
        return httpx.Response(self.status, content=self.body if self.body is not None else tile_png(z, x, y))

    def coordinates(self) -> list[tuple[int, int, int]]:
        return [tuple(int(p) for p in r.url.path.removesuffix(".png").strip("/").split("/")) for r in self.requests]


@pytest.fixture
def tiles() -> FakeTiles:
    return FakeTiles()


@pytest.fixture
async def client(settings, store, tiles):
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(tiles)))
    async with client_for(create_app(settings, store=store, tile_map=tile_map)) as c:
        yield c


def decoded(response: httpx.Response) -> Image.Image:
    assert response.headers["content-type"] == "image/png"
    image = Image.open(io.BytesIO(response.content))
    assert image.size == (IMAGE_PX, IMAGE_PX)
    return image.convert("RGB")


def test_world_pixel_places_the_equator_and_the_prime_meridian_at_the_middle():
    assert world_pixel(0, 0, 2) == (512, 512)
    assert world_pixel(0, 0, 0) == (128, 128)


def test_world_pixel_falls_in_the_known_tile_of_greenwich():
    x, y = world_pixel(51.4779, 0.0, 10)
    assert (int(x // TILE_PX), int(y // TILE_PX)) == (512, 340)


def test_world_pixel_spans_the_square_from_pole_to_pole():
    west, top = world_pixel(MAX_LAT, -180, 3)
    assert west == 0 and top == pytest.approx(0, abs=0.01)
    _, bottom = world_pixel(-MAX_LAT, 0, 3)
    assert bottom == pytest.approx(TILE_PX * 8, abs=0.01)


def test_world_pixel_clamps_the_latitude_and_wraps_the_longitude():
    assert world_pixel(90, 0, 3) == world_pixel(MAX_LAT, 0, 3)
    assert world_pixel(0, 190, 3)[0] == pytest.approx(world_pixel(0, -170, 3)[0])
    assert world_pixel(0, 180, 3)[0] == 0


def test_the_window_around_the_world_centre_touches_every_tile_of_zoom_2():
    window = Window.around(0, 0, 2)
    assert (window.left, window.top) == (0, 0)
    assert window.tiles() == {(x, y) for x in range(4) for y in range(4)}


def test_a_window_inside_one_tile_corner_touches_the_four_tiles_around_it():
    window = Window.around(0, 0, 5)
    assert window.tiles() == {(x, y) for x in range(14, 18) for y in range(14, 18)}


def test_a_window_across_the_antimeridian_wraps_to_the_far_column():
    window = Window.around(0, 179.999, 3)
    assert 8 in window.columns
    assert {x for x, _ in window.tiles()} == {6, 7, 0, 1}


def test_a_window_past_a_pole_has_no_tile_for_the_missing_rows():
    window = Window.around(MAX_LAT, 0, 3)
    assert -1 in window.rows
    assert window.tile(0, -1) is None
    assert {y for _, y in window.tiles()} == {0, 1}


async def test_the_world_is_a_1024_px_png_of_the_sixteen_zoom_2_tiles(client, tiles):
    image = decoded(await client.get("/map/world", headers=HEADERS))
    assert sorted(tiles.coordinates()) == [(2, x, y) for x in range(4) for y in range(4)]
    for x, y in ((0, 0), (3, 0), (0, 3), (3, 3), (1, 2)):
        assert image.getpixel((x * TILE_PX + 10, y * TILE_PX + 10)) == tile_colour(2, x, y)


async def test_the_area_centre_pixel_comes_from_the_tile_holding_the_point(client):
    lat, lon, zoom = 43.4723, -80.5449, 14
    image = decoded(await client.get("/map/area", params={"lat": lat, "lon": lon, "zoom": zoom}, headers=HEADERS))
    x, y = world_pixel(round(lat, 4), round(lon, 4), zoom)
    assert image.getpixel((512, 512)) == tile_colour(zoom, int(x // TILE_PX), int(y // TILE_PX))


async def test_the_area_aligns_neighbouring_tiles_to_the_centre(client):
    lat, lon, zoom = 43.4723, -80.5449, 14
    image = decoded(await client.get("/map/area", params={"lat": lat, "lon": lon, "zoom": zoom}, headers=HEADERS))
    x, y = world_pixel(round(lat, 4), round(lon, 4), zoom)
    cx, cy = round(x), round(y)
    for dx, dy in ((-512, -512), (511, -512), (-512, 511), (511, 511), (100, -300)):
        wx, wy = cx + dx, cy + dy
        assert image.getpixel((512 + dx, 512 + dy)) == tile_colour(zoom, wx // TILE_PX, wy // TILE_PX)


async def test_the_area_across_the_antimeridian_takes_its_east_side_from_the_first_column(client):
    image = decoded(await client.get("/map/area", params={"lat": 0, "lon": 179.999, "zoom": 3}, headers=HEADERS))
    assert image.getpixel((1023, 512)) == tile_colour(3, 1, 4)
    assert image.getpixel((600, 512)) == tile_colour(3, 0, 4)
    assert image.getpixel((500, 512)) == tile_colour(3, 7, 4)
    assert image.getpixel((0, 512)) == tile_colour(3, 6, 4)


async def test_the_area_past_a_pole_is_filled_with_a_plain_colour(client):
    image = decoded(await client.get("/map/area", params={"lat": 89, "lon": 0, "zoom": 3}, headers=HEADERS))
    assert image.getpixel((0, 0)) == OFF_MAP
    assert image.getpixel((1023, 100)) == OFF_MAP
    assert image.getpixel((512, 1023)) == tile_colour(3, 4, 1)


async def test_the_area_clamps_the_zoom_to_3_through_17(client, tiles):
    await client.get("/map/area", params={"lat": 10, "lon": 10, "zoom": 99}, headers=HEADERS)
    await client.get("/map/area", params={"lat": 10, "lon": 10, "zoom": -4}, headers=HEADERS)
    assert {z for z, _, _ in tiles.coordinates()} == {17, 3}


async def test_the_responses_may_be_cached_for_a_day(client):
    for path, params in (("/map/world", {}), ("/map/area", {"lat": 1, "lon": 2, "zoom": 6})):
        response = await client.get(path, params=params, headers=HEADERS)
        assert response.headers["cache-control"] == "max-age=86400"


async def test_tile_requests_carry_the_app_agent_and_only_tile_coordinates(client, tiles):
    await client.get("/map/area", params={"lat": 43.4723, "lon": -80.5449, "zoom": 12}, headers=HEADERS)
    assert tiles.requests
    for request in tiles.requests:
        assert (request.url.host, request.headers["user-agent"]) == ("tile.openstreetmap.org", USER_AGENT)
        assert request.url.query == b"" and not {k.lower() for k in request.headers} & {"x-edith-profile", "x-edith-token"}


async def test_at_most_two_tiles_are_in_flight(client, tiles):
    await client.get("/map/world", headers=HEADERS)
    await client.get("/map/area", params={"lat": 40, "lon": 20, "zoom": 9}, headers=HEADERS)
    assert tiles.most_in_flight == 2


async def test_the_same_area_is_not_fetched_or_stitched_twice(client, tiles):
    params = {"lat": 43.4723, "lon": -80.5449, "zoom": 12}
    first = await client.get("/map/area", params=params, headers=HEADERS)
    fetched = len(tiles.requests)
    second = await client.get("/map/area", params=params, headers=HEADERS)
    assert len(tiles.requests) == fetched
    assert second.content == first.content


async def test_points_that_round_to_the_same_4_decimals_share_one_image(client, tiles):
    await client.get("/map/area", params={"lat": 43.47231, "lon": -80.54491, "zoom": 12}, headers=HEADERS)
    fetched = len(tiles.requests)
    await client.get("/map/area", params={"lat": 43.47234, "lon": -80.54486, "zoom": 12}, headers=HEADERS)
    assert len(tiles.requests) == fetched


async def test_a_nearby_area_refetches_only_the_tiles_it_lacks(client, tiles):
    await client.get("/map/area", params={"lat": 43.4723, "lon": -80.5449, "zoom": 12}, headers=HEADERS)
    fetched = len(tiles.requests)
    await client.get("/map/area", params={"lat": 43.4723, "lon": -80.5400, "zoom": 12}, headers=HEADERS)
    assert len(tiles.requests) - fetched < fetched
    assert len(set(tiles.coordinates())) == len(tiles.requests)


async def test_the_tile_cache_stays_bounded(tiles):
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(tiles)))
    for lon in range(-170, 170, 3):
        await tile_map.area(0, lon, 3)
    assert len(tile_map._tiles) <= TILE_CACHE_MAX


async def test_tiles_that_cannot_be_fetched_give_a_502_that_is_not_cached(settings, store):
    down = FakeTiles(status=503)
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(down)))
    async with client_for(create_app(settings, store=store, tile_map=tile_map)) as client:
        for path, params in (("/map/world", {}), ("/map/area", {"lat": 1, "lon": 2, "zoom": 6})):
            response = await client.get(path, params=params, headers=HEADERS)
            assert response.status_code == 502
            assert response.json()["error"]["code"] == "map_failed"
            assert "cache-control" not in response.headers


async def test_a_tile_that_is_not_a_png_gives_a_502(settings, store):
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(FakeTiles(body=b"<html>blocked</html>"))))
    async with client_for(create_app(settings, store=store, tile_map=tile_map)) as client:
        response = await client.get("/map/world", headers=HEADERS)
        assert response.status_code == 502 and response.json()["error"]["code"] == "map_failed"


async def test_a_corrupt_png_gives_a_502_and_is_fetched_again_next_time(settings, store):
    corrupt = FakeTiles(body=b"\x89PNG\r\n\x1a\n" + b"not an image")
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(corrupt)))
    async with client_for(create_app(settings, store=store, tile_map=tile_map)) as client:
        assert (await client.get("/map/world", headers=HEADERS)).status_code == 502
        fetched = len(corrupt.requests)
        assert (await client.get("/map/world", headers=HEADERS)).status_code == 502
        assert len(corrupt.requests) > fetched


@pytest.mark.parametrize("params", [
    {"lat": "abc", "lon": 0, "zoom": 5},
    {"lat": 0, "lon": "nan", "zoom": 5},
    {"lat": "inf", "lon": 0, "zoom": 5},
    {"lat": 0, "lon": 0, "zoom": "x"},
    {"lat": 0, "lon": 0},
    {"lon": 0, "zoom": 5},
])
async def test_bad_numbers_give_a_422(client, tiles, params):
    response = await client.get("/map/area", params=params, headers=HEADERS)
    assert response.status_code == 422
    assert response.json()["error"]["code"] == "invalid_request"
    assert not tiles.requests


@pytest.mark.parametrize("path", ["/map/world", "/map/area?lat=1&lon=2&zoom=5"])
async def test_both_routes_need_the_device_token_and_a_profile(settings, store, tiles, path):
    settings = settings.model_copy(update={"device_token": SecretStr("secret")})
    tile_map = TileMap(httpx.AsyncClient(transport=httpx.MockTransport(tiles)))
    async with client_for(create_app(settings, store=store, tile_map=tile_map)) as client:
        assert (await client.get(path, headers=HEADERS)).status_code == 401
        assert (await client.get(path, headers={"X-Edith-Token": "wrong", **HEADERS})).status_code == 401
        no_profile = await client.get(path, headers={"X-Edith-Token": "secret"})
        assert (no_profile.status_code, no_profile.json()["error"]["code"]) == (400, "bad_profile")
        assert not tiles.requests
        assert (await client.get(path, headers={"X-Edith-Token": "secret", **HEADERS})).status_code == 200
