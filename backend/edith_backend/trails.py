"""Trails: the GPS fixes of each navigation, and which nearby areas a person has never walked.

Fixes go into trail_points, a Timescale hypertable when the extension exists and a plain table otherwise. The ground is
cut into square cells of CELL_DEG degrees; a cell counts as walked once any fix fell in it. Where Timescale is present
a continuous aggregate (trail_cells_daily, bucketed by time_bucket) answers which cells were walked; elsewhere the same
cells come from trail_points directly. Coordinates are rounded to 4 decimals before they are stored and are never
logged."""
import logging
import math
from dataclasses import dataclass
from typing import Protocol
from uuid import UUID

from psycopg_pool import AsyncConnectionPool

from .location import LocationFix, distance_m
from .navigate import Place, compass_word
from .route import bearing_deg

log = logging.getLogger("edith.trails")

# Must equal the cell size in migrations_trails/002_trail_cells.sql.
CELL_DEG = 0.002
# An area closer than this is the street the person is already on, not somewhere new.
MIN_AREA_M = 200.0
DEFAULT_RADIUS_M = 1500.0
MAX_RADIUS_M = 3000.0
MAX_AREAS = 5

Cell = tuple[int, int]


@dataclass(frozen=True)
class Area:
    lat: float
    lon: float
    distance_m: int
    bearing_deg: float
    compass: str


def cell_of(lat: float, lon: float) -> Cell:
    return math.floor(lat / CELL_DEG), math.floor(lon / CELL_DEG)


def cell_centre(cell: Cell) -> tuple[float, float]:
    return round((cell[0] + 0.5) * CELL_DEG, 4), round((cell[1] + 0.5) * CELL_DEG, 4)


def cell_window(lat: float, lon: float, radius_m: float) -> tuple[Cell, Cell]:
    """The lowest and highest cell of the square around the point that holds every cell within `radius_m` of it."""
    dlat = radius_m / 111_320
    dlon = radius_m / (111_320 * max(math.cos(math.radians(lat)), 0.05))
    return cell_of(lat - dlat, lon - dlon), cell_of(lat + dlat, lon + dlon)


def unexplored_areas(lat: float, lon: float, radius_m: float, walked: set[Cell], limit: int = MAX_AREAS) -> list[Area]:
    """The nearest cells within `radius_m` that are neither in `walked` nor the one the person stands in, nearest first,
    at least MIN_AREA_M away."""
    here = cell_of(lat, lon)
    low, high = cell_window(lat, lon, radius_m)
    areas: list[Area] = []
    for cell_lat in range(low[0], high[0] + 1):
        for cell_lon in range(low[1], high[1] + 1):
            if (cell_lat, cell_lon) in walked or (cell_lat, cell_lon) == here:
                continue
            clat, clon = cell_centre((cell_lat, cell_lon))
            distance = distance_m(lat, lon, clat, clon)
            if MIN_AREA_M <= distance <= radius_m:
                bearing = bearing_deg(lat, lon, clat, clon)
                areas.append(Area(clat, clon, round(distance), round(bearing, 1), compass_word(bearing)))
    return sorted(areas, key=lambda a: a.distance_m)[:limit]


def prefer_unwalked(places: list[Place], walked: set[Cell], here: Cell) -> tuple[list[Place], bool]:
    """The places with the first one lying in a cell nobody has walked moved to the front. The flag says whether there
    was one; when not, the places come back as they were."""
    for i, place in enumerate(places):
        cell = cell_of(place.lat, place.lon)
        if cell not in walked and cell != here:
            return [place, *places[:i], *places[i + 1:]], True
    return places, False


class TrailStore(Protocol):
    async def open(self) -> None: ...
    async def close(self) -> None: ...
    async def record(self, profile_id: UUID, adventure_id: UUID, fix: LocationFix) -> None: ...
    async def walked_cells(self, profile_id: UUID, lat: float, lon: float, radius_m: float) -> set[Cell] | None:
        """The cells walked inside the window around the point, or None when trails are not kept or could not be read."""
        ...


class DisabledTrailStore:
    async def open(self) -> None:
        pass

    async def close(self) -> None:
        pass

    async def record(self, profile_id: UUID, adventure_id: UUID, fix: LocationFix) -> None:
        pass

    async def walked_cells(self, profile_id: UUID, lat: float, lon: float, radius_m: float) -> set[Cell] | None:
        return None


class PostgresTrailStore:
    def __init__(self, database_url: str, max_size: int = 2) -> None:
        self._pool = AsyncConnectionPool(
            database_url, min_size=1, max_size=max_size, open=False,
            check=AsyncConnectionPool.check_connection,
            kwargs={"connect_timeout": 5, "prepare_threshold": None},
        )
        self._cells_view: bool | None = None

    async def open(self) -> None:
        await self._pool.open(wait=False)

    async def close(self) -> None:
        await self._pool.close()

    async def record(self, profile_id: UUID, adventure_id: UUID, fix: LocationFix) -> None:
        """Best effort: a fix that cannot be saved is dropped, since the trail only informs a later suggestion."""
        try:
            async with self._pool.connection(timeout=5) as conn:
                await conn.execute(
                    "insert into trail_points (profile_id, adventure_id, taken_at, lat, lon, accuracy_m)"
                    " values (%s, %s, %s, %s, %s, %s) on conflict (adventure_id, taken_at) do nothing",
                    (profile_id, adventure_id, fix.taken_at, round(fix.lat, 4), round(fix.lon, 4), fix.accuracy_m))
        except Exception as exc:
            log.warning("could not record a trail point: %s", type(exc).__name__)

    async def walked_cells(self, profile_id: UUID, lat: float, lon: float, radius_m: float) -> set[Cell] | None:
        (low_lat, low_lon), (high_lat, high_lon) = cell_window(lat, lon, radius_m)
        try:
            async with self._pool.connection(timeout=5) as conn:
                if self._cells_view is None:
                    row = await (await conn.execute("select to_regclass('trail_cells_daily') is not null")).fetchone()
                    self._cells_view = bool(row and row[0])
                if self._cells_view:
                    query = ("select distinct cell_lat, cell_lon from trail_cells_daily where profile_id = %s"
                             " and cell_lat between %s and %s and cell_lon between %s and %s")
                else:
                    query = ("select distinct floor(lat / %(deg)s)::int, floor(lon / %(deg)s)::int from trail_points"
                             " where profile_id = %(p)s and lat >= %(lat0)s and lat < %(lat1)s"
                             " and lon >= %(lon0)s and lon < %(lon1)s")
                if self._cells_view:
                    cursor = await conn.execute(query, (profile_id, low_lat, high_lat, low_lon, high_lon))
                else:
                    cursor = await conn.execute(query, {
                        "deg": CELL_DEG, "p": profile_id, "lat0": low_lat * CELL_DEG, "lat1": (high_lat + 1) * CELL_DEG,
                        "lon0": low_lon * CELL_DEG, "lon1": (high_lon + 1) * CELL_DEG})
                return {(a, b) for a, b in await cursor.fetchall()}
        except Exception as exc:
            log.warning("could not read walked cells: %s", type(exc).__name__)
            return None


def make_trail_store(database_url: str | None) -> TrailStore:
    return PostgresTrailStore(database_url) if database_url else DisabledTrailStore()
