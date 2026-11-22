"""Walking routes from Valhalla's OpenStreetMap server, and the pure maths that follows one.

A Route is the polyline Valhalla returns plus its turns. `guide` takes the person's newest fix and says which point of
the route to head for, how far off the route they are, and the next turn and how far it is. Coordinates leave the backend
rounded to 4 decimals (about 11 m) and are never logged."""
import logging
import math
from dataclasses import dataclass
from typing import Protocol

import httpx

from .location import distance_m
from .resolve import SERVICE_TIMEOUT_S, USER_AGENT

log = logging.getLogger("edith.route")

VALHALLA_URL = "https://valhalla1.openstreetmap.de/route"
# Further than this from the route, the route no longer describes where the person is and a new one is asked for.
OFF_ROUTE_M = 30.0
# The arrow points at the first route vertex at least this far ahead of the person, so it does not swing about when the
# next vertex is a few metres away.
LOOKAHEAD_M = 30.0
# Turns this close behind the person's position along the route are counted as taken.
TURN_PASSED_M = 3.0
ROUTE_TIMEOUT_S = 4.0

_EARTH_M = 6371000.0

# Valhalla maneuver types, folded into the few the headset can show. Types it does not list are treated as straight.
_KINDS = {
    1: "start", 2: "start", 3: "start", 4: "arrive", 5: "arrive", 6: "arrive",
    9: "slight_right", 10: "right", 11: "sharp_right", 12: "uturn", 13: "uturn",
    14: "sharp_left", 15: "left", 16: "slight_left",
}


class RouteFailed(Exception):
    """Valhalla did not give a usable route."""


@dataclass(frozen=True)
class Turn:
    kind: str
    # Index of the route vertex where the turn is made.
    index: int


@dataclass(frozen=True)
class Route:
    points: tuple[tuple[float, float], ...]
    # Metres from the start to each point.
    along: tuple[float, ...]
    turns: tuple[Turn, ...]

    @property
    def length_m(self) -> float:
        return self.along[-1]


@dataclass(frozen=True)
class Guidance:
    off_route: bool
    along_m: float
    off_m: float
    waypoint_lat: float
    waypoint_lon: float
    waypoint_distance_m: float
    waypoint_bearing_deg: float
    remaining_m: float
    # The next turn ahead, or the arrival when none is left.
    turn: str
    turn_distance_m: float


class Router(Protocol):
    async def route(self, lat: float, lon: float, to_lat: float, to_lon: float) -> Route: ...


def decode_polyline(shape: str, precision: int = 6) -> list[tuple[float, float]]:
    """Google's encoded polyline algorithm; Valhalla uses 6 decimal digits."""
    factor = 10.0 ** precision
    points: list[tuple[float, float]] = []
    index = lat = lon = 0
    while index < len(shape):
        deltas = []
        for _ in range(2):
            result = shift = 0
            while True:
                byte = ord(shape[index]) - 63
                index += 1
                result |= (byte & 0x1F) << shift
                shift += 5
                if byte < 0x20:
                    break
            deltas.append(~(result >> 1) if result & 1 else result >> 1)
        lat += deltas[0]
        lon += deltas[1]
        points.append((lat / factor, lon / factor))
    return points


def parse_route(reply: dict) -> Route:
    """The route in a Valhalla /route reply. Raises RouteFailed when the reply does not hold one."""
    try:
        leg = reply["trip"]["legs"][0]
        points = decode_polyline(leg["shape"])
        maneuvers = leg["maneuvers"]
        turns = tuple(Turn(_KINDS.get(m["type"], "straight"), int(m["begin_shape_index"])) for m in maneuvers)
    except (KeyError, IndexError, TypeError, ValueError) as exc:
        raise RouteFailed("unreadable route") from exc
    if len(points) < 2 or any(not 0 <= t.index < len(points) for t in turns):
        raise RouteFailed("unreadable route")
    along = [0.0]
    for a, b in zip(points, points[1:]):
        along.append(along[-1] + distance_m(*a, *b))
    return Route(tuple(points), tuple(along), turns)


def bearing_deg(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """The initial great-circle bearing from the first point to the second, clockwise from true north, in [0, 360)."""
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dl = math.radians(lon2 - lon1)
    x = math.sin(dl) * math.cos(p2)
    y = math.cos(p1) * math.sin(p2) - math.sin(p1) * math.cos(p2) * math.cos(dl)
    return (math.degrees(math.atan2(x, y)) + 360.0) % 360.0


def locate(route: Route, lat: float, lon: float) -> tuple[float, float]:
    """Metres along the route of the nearest point on it, and the distance to that point."""
    cos_lat = math.cos(math.radians(lat))
    best_off, best_along = math.inf, 0.0
    for i in range(len(route.points) - 1):
        # Metres east and north of the fix, flat over a segment's length.
        ax = math.radians(route.points[i][1] - lon) * cos_lat * _EARTH_M
        ay = math.radians(route.points[i][0] - lat) * _EARTH_M
        bx = math.radians(route.points[i + 1][1] - lon) * cos_lat * _EARTH_M
        by = math.radians(route.points[i + 1][0] - lat) * _EARTH_M
        dx, dy = bx - ax, by - ay
        length_sq = dx * dx + dy * dy
        t = 0.0 if length_sq == 0 else min(1.0, max(0.0, -(ax * dx + ay * dy) / length_sq))
        off = math.hypot(ax + t * dx, ay + t * dy)
        if off < best_off:
            best_off = off
            best_along = route.along[i] + t * (route.along[i + 1] - route.along[i])
    return best_along, best_off


def guide(route: Route, lat: float, lon: float) -> Guidance:
    along, off = locate(route, lat, lon)
    ahead = next((i for i, a in enumerate(route.along) if a >= along + LOOKAHEAD_M), len(route.points) - 1)
    wlat, wlon = route.points[ahead]
    turn = next((t for t in route.turns
                 if t.kind not in ("start", "straight") and route.along[t.index] > along + TURN_PASSED_M), None)
    return Guidance(
        off_route=off > OFF_ROUTE_M, along_m=along, off_m=off, waypoint_lat=wlat, waypoint_lon=wlon,
        waypoint_distance_m=distance_m(lat, lon, wlat, wlon), waypoint_bearing_deg=bearing_deg(lat, lon, wlat, wlon),
        remaining_m=max(0.0, route.length_m - along),
        turn=turn.kind if turn else "arrive",
        turn_distance_m=max(0.0, (route.along[turn.index] if turn else route.length_m) - along))


class HttpRouter:
    def __init__(self, http: httpx.AsyncClient, url: str = VALHALLA_URL) -> None:
        self._http, self._url = http, url

    async def route(self, lat: float, lon: float, to_lat: float, to_lon: float) -> Route:
        body = {"locations": [{"lat": round(lat, 4), "lon": round(lon, 4)},
                              {"lat": round(to_lat, 4), "lon": round(to_lon, 4)}],
                "costing": "pedestrian"}
        try:
            response = await self._http.post(self._url, json=body, timeout=min(ROUTE_TIMEOUT_S, SERVICE_TIMEOUT_S),
                                             headers={"User-Agent": USER_AGENT})
            response.raise_for_status()
            return parse_route(response.json())
        except (httpx.HTTPError, ValueError) as exc:
            log.info("route failed: %s", type(exc).__name__)
            raise RouteFailed from exc
