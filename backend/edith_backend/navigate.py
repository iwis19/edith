"""Finds a place near the person and keeps it as their navigation target, so the headset can point an arrow at it.

Ask's Gemini session calls the find_place tool; the headset passes the call to POST /navigate, which searches
OpenStreetMap around the phone's latest fix and remembers the nearest match. GET /navigate then gives the distance and
compass bearing from the newest fix to that target, with the phone's heading so the headset can find north. Coordinates
leave the backend rounded to 4 decimals (about 11 m), as in resolve.py, and are never logged."""
import asyncio
import logging
import math
import time
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from typing import Literal, Protocol, get_args
from uuid import UUID

import httpx
from pydantic import BaseModel, Field

from .location import LocationFix, distance_m
from .resolve import NOMINATIM_GAP_S, OVERPASS_URLS, SERVICE_TIMEOUT_S, USER_AGENT, clean_name, normalise

log = logging.getLogger("edith.navigate")

NOMINATIM_SEARCH_URL = "https://nominatim.openstreetmap.org/search"
# Searched in turn until one finds something: a short walk first, then a long one.
SEARCH_RADII_M = (1500, 5000)
MAX_CHOICES = 3
# A target is forgotten this long after it was set, so a session left running does not guide anyone forever.
TARGET_LIFE_S = 2 * 3600
# The fix a search starts from must be this fresh: a position from half an hour ago is the wrong place to search near.
SEARCH_FIX_S = 120.0
# The whole search, every service and radius together. The headset gives the call a little longer.
NAVIGATE_TIMEOUT_S = 8.0
# Within this distance, or the fix's accuracy when that is worse (up to the cap), the person has arrived.
ARRIVED_M, ARRIVED_CAP_M = 20.0, 50.0

Category = Literal[
    "any", "restaurant", "cafe", "fast_food", "food", "bar", "subway", "train", "bus", "transit", "landmark", "museum",
    "park", "library", "pharmacy", "grocery", "convenience", "atm", "bank", "washroom", "hospital", "parking", "hotel",
    "gas", "shopping", "university", "place_of_worship",
]
CATEGORIES: tuple[str, ...] = get_args(Category)

# Overpass filters per category, each applied to nodes, ways and relations. A filter without ["name"] also finds
# unnamed features, which are worth pointing at for these kinds (an entrance, a washroom, a cash machine).
_FILTERS: dict[str, tuple[str, ...]] = {
    "restaurant": ('["amenity"="restaurant"]["name"]',),
    "cafe": ('["amenity"="cafe"]["name"]',),
    "fast_food": ('["amenity"="fast_food"]["name"]',),
    "food": ('["amenity"~"^(restaurant|cafe|fast_food|food_court)$"]["name"]',),
    "bar": ('["amenity"~"^(bar|pub|biergarten)$"]["name"]',),
    "subway": ('["railway"="station"]["station"="subway"]', '["public_transport"="station"]["subway"="yes"]',
               '["railway"="subway_entrance"]'),
    "train": ('["railway"="station"]["name"]', '["railway"="halt"]["name"]'),
    "bus": ('["highway"="bus_stop"]', '["amenity"="bus_station"]'),
    "transit": ('["public_transport"="station"]["name"]', '["railway"="station"]["name"]', '["highway"="bus_stop"]',
                '["railway"="subway_entrance"]', '["railway"="tram_stop"]["name"]'),
    "landmark": ('["tourism"~"^(attraction|museum|viewpoint|artwork|gallery)$"]["name"]', '["historic"]["name"]',
                 '["man_made"="tower"]["name"]["wikidata"]'),
    "museum": ('["tourism"~"^(museum|gallery)$"]["name"]',),
    "park": ('["leisure"~"^(park|garden|nature_reserve)$"]["name"]',),
    "library": ('["amenity"="library"]',),
    "pharmacy": ('["amenity"="pharmacy"]', '["healthcare"="pharmacy"]'),
    "grocery": ('["shop"~"^(supermarket|greengrocer|grocery)$"]',),
    "convenience": ('["shop"="convenience"]',),
    "atm": ('["amenity"="atm"]', '["atm"="yes"]["amenity"="bank"]'),
    "bank": ('["amenity"="bank"]["name"]',),
    "washroom": ('["amenity"="toilets"]',),
    "hospital": ('["amenity"~"^(hospital|clinic)$"]["name"]',),
    "parking": ('["amenity"="parking"]',),
    "hotel": ('["tourism"~"^(hotel|hostel|motel|guest_house)$"]["name"]',),
    "gas": ('["amenity"="fuel"]',),
    "shopping": ('["shop"="mall"]["name"]', '["shop"="department_store"]["name"]'),
    "university": ('["amenity"~"^(university|college)$"]["name"]', '["building"="university"]["name"]'),
    "place_of_worship": ('["amenity"="place_of_worship"]["name"]',),
}

# What an unnamed feature is called, by the tag that matched it.
_UNNAMED = {
    ("railway", "subway_entrance"): "Subway entrance",
    ("highway", "bus_stop"): "Bus stop",
    ("amenity", "toilets"): "Washroom",
    ("amenity", "atm"): "ATM",
    ("amenity", "parking"): "Parking",
    ("amenity", "fuel"): "Gas station",
    ("amenity", "library"): "Library",
    ("amenity", "pharmacy"): "Pharmacy",
    ("shop", "convenience"): "Convenience store",
    ("shop", "supermarket"): "Supermarket",
}


class Place(BaseModel):
    name: str
    kind: str
    lat: float
    lon: float
    distance_m: int


class NavigateRequest(BaseModel):
    """What Gemini's find_place call asks for: a kind of place, a name, or both."""
    category: Category = "any"
    name: str | None = Field(None, max_length=100)


class NavStatus(BaseModel):
    """What the headset polls. Everything but `active` is missing while nothing is being navigated to."""
    active: bool
    name: str | None = None
    kind: str | None = None
    distance_m: int | None = None
    # From the newest fix to the target, in degrees clockwise from true north.
    bearing_deg: float | None = None
    # "north-east" and the like, for speech.
    compass: str | None = None
    arrived: bool = False
    # Seconds since the backend received the newest fix, and what came with it.
    fix_age_s: float | None = None
    accuracy_m: float | None = None
    heading_deg: float | None = None
    heading_accuracy_deg: float | None = None
    course_deg: float | None = None
    speed_mps: float | None = None


class NavigateReply(BaseModel):
    target: NavStatus
    # The other nearest matches, nearest first, without coordinates, so EDITH can mention them.
    others: list[dict]


class NoPlace(Exception):
    """Nothing matched near the person."""


class SearchFailed(Exception):
    """Every search service failed."""


class PlaceFinder(Protocol):
    async def find(self, lat: float, lon: float, request: NavigateRequest) -> list[Place]: ...


def bearing_deg(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """The initial great-circle bearing from the first point to the second, clockwise from true north, in [0, 360)."""
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dl = math.radians(lon2 - lon1)
    x = math.sin(dl) * math.cos(p2)
    y = math.cos(p1) * math.sin(p2) - math.sin(p1) * math.cos(p2) * math.cos(dl)
    return (math.degrees(math.atan2(x, y)) + 360.0) % 360.0


_COMPASS = ("north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west")


def compass_word(bearing: float) -> str:
    return _COMPASS[int((bearing % 360 + 22.5) // 45) % 8]


def arrived(distance: float, accuracy_m: float) -> bool:
    return distance <= max(ARRIVED_M, min(accuracy_m, ARRIVED_CAP_M))


def overpass_query(lat: float, lon: float, radius_m: int, category: str) -> str:
    around = f"(around:{radius_m},{lat},{lon})"
    parts = "".join(f"nwr{around}{f};" for f in _FILTERS[category])
    return f"[out:json][timeout:8];({parts});out center tags 200;"


def _kind(tags: dict) -> str:
    for key in ("amenity", "tourism", "shop", "leisure", "historic", "railway", "highway", "public_transport",
                "healthcare", "man_made", "building"):
        if value := tags.get(key):
            if key == "railway" and value == "station" and tags.get("station") == "subway":
                return "subway station"
            if key == "historic" and value == "yes":
                return "historic site"
            return value.replace("_", " ")
    return "place"


def _unnamed(tags: dict) -> str | None:
    return next((label for (key, value), label in _UNNAMED.items() if tags.get(key) == value), None)


def overpass_places(reply: dict, lat: float, lon: float) -> list[Place]:
    """The features in an Overpass reply as places, nearest first. An unnamed subway entrance takes the name of the
    nearest subway station in the reply."""
    raw: list[tuple[dict, float, float]] = []
    for element in reply.get("elements", []):
        point = element if "lat" in element else element.get("center") or {}
        if "lat" in point:
            raw.append((element.get("tags") or {}, point["lat"], point["lon"]))
    stations = [(clean_name(t["name"]), plat, plon) for t, plat, plon in raw
                if t.get("name") and (t.get("station") == "subway" or t.get("subway") == "yes")]
    places: list[Place] = []
    for tags, plat, plon in raw:
        name = clean_name(tags.get("name") or "")
        if not name and tags.get("railway") == "subway_entrance" and stations:
            station = min(stations, key=lambda s: distance_m(plat, plon, s[1], s[2]))
            if distance_m(plat, plon, station[1], station[2]) <= 400:
                name = f"{station[0]} entrance"
        name = name or _unnamed(tags) or ""
        if not name:
            continue
        places.append(Place(name=name, kind=_kind(tags), lat=plat, lon=plon,
                            distance_m=round(distance_m(lat, lon, plat, plon))))
    return sorted(places, key=lambda p: p.distance_m)


def nominatim_places(reply: list, lat: float, lon: float) -> list[Place]:
    places: list[Place] = []
    for item in reply if isinstance(reply, list) else []:
        try:
            plat, plon = float(item["lat"]), float(item["lon"])
        except (KeyError, TypeError, ValueError):
            continue
        name = clean_name(item.get("name") or (item.get("display_name") or "").split(",")[0])
        if not name:
            continue
        kind = (item.get("type") or item.get("category") or "place").replace("_", " ")
        places.append(Place(name=name, kind=kind, lat=plat, lon=plon, distance_m=round(distance_m(lat, lon, plat, plon))))
    return sorted(places, key=lambda p: p.distance_m)


def matches_name(place: Place, name: str) -> bool:
    """Every word of the asked name appears in the place's name, so "tim hortons" finds "Tim Hortons" and "Tim
    Horton's" alike."""
    words = normalise(name).split()
    have = normalise(place.name)
    return bool(words) and all(w in have for w in words)


def dedupe(places: list[Place]) -> list[Place]:
    """One place per name within 60 m: a station is often a node, a way and a relation at once."""
    kept: list[Place] = []
    for place in places:
        if not any(normalise(k.name) == normalise(place.name) and distance_m(k.lat, k.lon, place.lat, place.lon) < 60
                   for k in kept):
            kept.append(place)
    return kept


class HttpPlaceFinder:
    """Overpass for a kind of place, Nominatim for a name Overpass did not find."""

    def __init__(self, http: httpx.AsyncClient, sleep: Callable[[float], Awaitable] = asyncio.sleep,
                 clock: Callable[[], float] = time.monotonic) -> None:
        self._http, self._sleep, self._clock = http, sleep, clock
        self._nominatim_lock = asyncio.Lock()
        self._nominatim_last: float | None = None

    async def find(self, lat: float, lon: float, request: NavigateRequest) -> list[Place]:
        lat, lon = round(lat, 4), round(lon, 4)
        name = (request.name or "").strip()
        # Raised only when no service answered at all: an empty answer is NoPlace, not a failure.
        answered = False
        if request.category != "any":
            for radius in SEARCH_RADII_M:
                try:
                    found = await self._overpass(lat, lon, radius, request.category)
                except (httpx.HTTPError, ValueError) as exc:
                    log.info("overpass search failed: %s", type(exc).__name__)
                    break
                answered = True
                if name:
                    found = [p for p in found if matches_name(p, name)]
                if found:
                    return dedupe(found)[:MAX_CHOICES]
        if name:
            try:
                found = await self._nominatim(lat, lon, name)
                answered = True
                if found:
                    return dedupe(found)[:MAX_CHOICES]
            except (httpx.HTTPError, ValueError) as exc:
                log.info("nominatim search failed: %s", type(exc).__name__)
        if not answered:
            raise SearchFailed
        return []

    async def _overpass(self, lat: float, lon: float, radius: int, category: str) -> list[Place]:
        last: Exception | None = None
        for url in OVERPASS_URLS:
            try:
                response = await self._http.post(url, data={"data": overpass_query(lat, lon, radius, category)},
                                                 timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": USER_AGENT})
                response.raise_for_status()
                reply = response.json()
                if isinstance(reply, dict) and reply.get("remark"):
                    raise ValueError("Overpass reported a runtime error.")
                return overpass_places(reply, lat, lon)
            except (httpx.HTTPError, ValueError) as exc:
                last = exc
        assert last is not None
        raise last

    async def _nominatim(self, lat: float, lon: float, name: str) -> list[Place]:
        # About 5 km each way, and only results inside it.
        box = f"{lon - 0.06},{lat + 0.045},{lon + 0.06},{lat - 0.045}"
        async with self._nominatim_lock:
            if self._nominatim_last is not None and (wait := self._nominatim_last + NOMINATIM_GAP_S - self._clock()) > 0:
                await self._sleep(wait)
            try:
                response = await self._http.get(
                    NOMINATIM_SEARCH_URL, timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": USER_AGENT},
                    params={"q": name, "format": "jsonv2", "viewbox": box, "bounded": 1, "limit": 10,
                            "accept-language": "en"})
                response.raise_for_status()
                return nominatim_places(response.json(), lat, lon)
            finally:
                self._nominatim_last = self._clock()


@dataclass
class _Target:
    place: Place
    set_at: float


class Navigations:
    """Each profile's navigation target, in memory like the positions it is measured from."""

    def __init__(self, clock: Callable[[], float] = time.time) -> None:
        self._clock = clock
        self._targets: dict[UUID, _Target] = {}

    def set(self, profile_id: UUID, place: Place) -> None:
        self._targets[profile_id] = _Target(place, self._clock())
        log.info("navigating to %r (%s)", place.name, place.kind)

    def clear(self, profile_id: UUID) -> bool:
        return self._targets.pop(profile_id, None) is not None

    def target(self, profile_id: UUID) -> Place | None:
        found = self._targets.get(profile_id)
        if found is None:
            return None
        if self._clock() - found.set_at > TARGET_LIFE_S:
            del self._targets[profile_id]
            return None
        return found.place

    def active(self, profile_id: UUID) -> bool:
        return self.target(profile_id) is not None


def status(place: Place | None, latest: tuple[LocationFix, float] | None) -> NavStatus:
    if place is None:
        return NavStatus(active=False)
    if latest is None:
        return NavStatus(active=True, name=place.name, kind=place.kind)
    fix, age = latest
    distance = distance_m(fix.lat, fix.lon, place.lat, place.lon)
    bearing = bearing_deg(fix.lat, fix.lon, place.lat, place.lon)
    return NavStatus(
        active=True, name=place.name, kind=place.kind, distance_m=round(distance), bearing_deg=round(bearing, 1),
        compass=compass_word(bearing), arrived=arrived(distance, fix.accuracy_m), fix_age_s=round(age, 2),
        accuracy_m=fix.accuracy_m, heading_deg=fix.heading_deg, heading_accuracy_deg=fix.heading_accuracy_deg,
        course_deg=fix.course_deg, speed_mps=fix.speed_mps)


def others(places: list[Place]) -> list[dict]:
    return [{"name": p.name, "kind": p.kind, "distance_m": p.distance_m} for p in places[1:]]
