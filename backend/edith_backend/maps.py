"""Stitched OpenStreetMap images for the Scrapbook's globe and city map. Only tile coordinates leave the backend, and
the User-Agent names the app and its version only."""
import asyncio
import io
import logging
import math
from collections import OrderedDict
from dataclasses import dataclass

import httpx
from PIL import Image, UnidentifiedImageError

from .resolve import USER_AGENT

log = logging.getLogger("edith.maps")

TILE_URL = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
TILE_PX = 256
IMAGE_PX = 1024
WORLD_ZOOM = 2
MIN_ZOOM, MAX_ZOOM = 3, 17
# The latitude where the Web Mercator square ends.
MAX_LAT = 85.0511
TILE_TIMEOUT_S = 5.0
# The tile usage policy asks for no heavy use, so a burst of tiles is fetched two at a time.
MAX_TILES_IN_FLIGHT = 2
TILE_CACHE_MAX = 512
IMAGE_CACHE_MAX = 32
# Where the window runs past the poles, close to the sea colour of the tiles beside it.
OFF_MAP = (170, 211, 223)
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class MapError(Exception):
    pass


def clamp_lat(lat: float) -> float:
    return max(-MAX_LAT, min(MAX_LAT, lat))


def wrap_lon(lon: float) -> float:
    return (lon + 180) % 360 - 180


def world_pixel(lat: float, lon: float, zoom: int) -> tuple[float, float]:
    """The slippy-map world pixel of a point at a tile zoom: x from the antimeridian's west edge, y from the top."""
    size = TILE_PX * 2**zoom
    rad = math.radians(clamp_lat(lat))
    x = (wrap_lon(lon) + 180) / 360 * size
    y = (1 - math.asinh(math.tan(rad)) / math.pi) / 2 * size
    return x, y


@dataclass(frozen=True)
class Window:
    """The IMAGE_PX square of world pixels whose top-left corner is (left, top), and the tiles it touches. A column
    outside 0..2**zoom-1 wraps across the antimeridian, and a row outside it is past a pole."""
    zoom: int
    left: int
    top: int

    @classmethod
    def around(cls, lat: float, lon: float, zoom: int) -> "Window":
        x, y = world_pixel(lat, lon, zoom)
        return cls(zoom, round(x) - IMAGE_PX // 2, round(y) - IMAGE_PX // 2)

    @property
    def columns(self) -> range:
        return range(self.left // TILE_PX, (self.left + IMAGE_PX - 1) // TILE_PX + 1)

    @property
    def rows(self) -> range:
        return range(self.top // TILE_PX, (self.top + IMAGE_PX - 1) // TILE_PX + 1)

    def tile(self, column: int, row: int) -> tuple[int, int] | None:
        """The tile's (x, y) in the tile grid, or None past a pole."""
        count = 2**self.zoom
        return (column % count, row) if 0 <= row < count else None

    def tiles(self) -> set[tuple[int, int]]:
        return {t for c in self.columns for r in self.rows if (t := self.tile(c, r))}


def compose(window: Window, tiles: dict[tuple[int, int], bytes]) -> bytes:
    """The window's PNG from the PNG bytes of every tile it touches."""
    canvas = Image.new("RGB", (IMAGE_PX, IMAGE_PX), OFF_MAP)
    for column in window.columns:
        for row in window.rows:
            if (key := window.tile(column, row)) is not None:
                with Image.open(io.BytesIO(tiles[key])) as tile:
                    canvas.paste(tile.convert("RGB"), (column * TILE_PX - window.left, row * TILE_PX - window.top))
    out = io.BytesIO()
    canvas.save(out, "PNG")
    return out.getvalue()


class TileMap:
    def __init__(self, http: httpx.AsyncClient) -> None:
        self._http = http
        self._slots = asyncio.Semaphore(MAX_TILES_IN_FLIGHT)
        self._tiles: OrderedDict[tuple[int, int, int], bytes] = OrderedDict()
        self._images: OrderedDict[tuple[float, float, int], bytes] = OrderedDict()

    async def world(self) -> bytes:
        """The whole Web Mercator world at tile zoom 2."""
        return await self._image(0.0, 0.0, WORLD_ZOOM)

    async def area(self, lat: float, lon: float, zoom: int) -> bytes:
        """The image centred on a point, which is first clamped, wrapped and rounded to 4 decimals (about 11 m)."""
        return await self._image(
            round(clamp_lat(lat), 4), round(wrap_lon(lon), 4), max(MIN_ZOOM, min(MAX_ZOOM, zoom)))

    async def _image(self, lat: float, lon: float, zoom: int) -> bytes:
        key = (lat, lon, zoom)
        if (found := self._images.get(key)) is not None:
            self._images.move_to_end(key)
            return found
        window = Window.around(lat, lon, zoom)
        wanted = sorted(window.tiles())
        fetched = await asyncio.gather(*(self._tile(zoom, x, y) for x, y in wanted), return_exceptions=True)
        for result in fetched:
            if isinstance(result, BaseException):
                raise result
        try:
            png = await asyncio.to_thread(compose, window, dict(zip(wanted, fetched)))
        except (UnidentifiedImageError, OSError) as exc:
            for x, y in wanted:
                self._tiles.pop((zoom, x, y), None)
            raise MapError(f"A tile could not be decoded ({type(exc).__name__}).") from exc
        self._images[key] = png
        while len(self._images) > IMAGE_CACHE_MAX:
            self._images.popitem(last=False)
        return png

    async def _tile(self, zoom: int, x: int, y: int) -> bytes:
        key = (zoom, x, y)
        if (found := self._tiles.get(key)) is not None:
            self._tiles.move_to_end(key)
            return found
        async with self._slots:
            try:
                response = await self._http.get(
                    TILE_URL.format(z=zoom, x=x, y=y), timeout=TILE_TIMEOUT_S, headers={"User-Agent": USER_AGENT})
                response.raise_for_status()
            except httpx.HTTPError as exc:
                log.warning("tile %d/%d/%d failed: %s", zoom, x, y, type(exc).__name__)
                raise MapError(f"A tile could not be fetched ({type(exc).__name__}).") from exc
        if not response.content.startswith(PNG_SIGNATURE):
            raise MapError("A tile was not a PNG.")
        self._tiles[key] = response.content
        while len(self._tiles) > TILE_CACHE_MAX:
            self._tiles.popitem(last=False)
        return response.content
