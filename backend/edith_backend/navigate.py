"""Finds a place near the person and keeps it as their navigation target, so the headset can point an arrow at it.

Ask's Gemini session calls the find_place tool; the headset passes the call to POST /navigate, which searches
OpenStreetMap around the phone's latest fix and remembers the nearest match. GET /navigate then gives the distance and
compass bearing from the newest fix to that target, with the phone's heading so the headset can find north. Coordinates
leave the backend rounded to 4 decimals (about 11 m), as in resolve.py, and are never logged."""
import asyncio
import logging
import time
from collections import OrderedDict
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from typing import Literal, Protocol, get_args
from uuid import UUID, uuid4

import httpx
from pydantic import BaseModel, Field

from .location import LocationFix, distance_m
from .resolve import NOMINATIM_GAP_S, OVERPASS_URLS, SERVICE_TIMEOUT_S, USER_AGENT, clean_name, normalise
from .route import ROUTE_TIMEOUT_S, Guidance, Route, RouteFailed, Router, bearing_deg, guide

log = logging.getLogger("edith.navigate")

NOMINATIM_SEARCH_URL = "https://nominatim.openstreetmap.org/search"
NOMINATIM_REVERSE_URL = "https://nominatim.openstreetmap.org/reverse"
# An Overpass mirror that has not answered in this long gets company from the next mirror.
OVERPASS_HEDGE_S = 1.5
# Searched in turn until one finds something: a short walk first, then a long one. A 5 km Overpass query does not
# finish within NAVIGATE_TIMEOUT_S on the public mirrors, so the long one is 3 km.
SEARCH_RADII_M = (1500, 3000)
MAX_CHOICES = 3
# The finder stops this long before NAVIGATE_TIMEOUT_S and answers with what it has, so a slow wide search ends in
# "nothing found nearby" rather than a failed call.
SEARCH_MARGIN_S = 0.5
# A wide search is not started with less than this left: it would not finish.
WIDE_MIN_S = 1.5
# The same search from within about 100 m reuses its places for this long, with distances from the new fix.
SEARCH_CACHE_S = 30 * 60
# A target's address looked up after the search gives up after this long; the card simply shows none.
ADDRESS_TIMEOUT_S = 6.0
SEARCH_CACHE_MAX = 256
# A name search often finds a stop named after a building; those count only when transit was asked for.
TRANSIT = ("bus", "transit", "train", "subway")
STOP_KINDS = ("bus stop", "platform", "stop position", "stop", "station")
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
    # The street address, "1151 Richmond St", when the map has one.
    address: str | None = None


class NavigateRequest(BaseModel):
    """What Gemini's find_place call asks for: a kind of place, a name, or both."""
    category: Category = "any"
    name: str | None = Field(None, max_length=100)
    # Prefer a match in an area the person has never walked, when their trail is known.
    new_area: bool = False


class NavStatus(BaseModel):
    """What the headset polls. Everything but `active` is missing while nothing is being navigated to."""
    active: bool
    name: str | None = None
    kind: str | None = None
    address: str | None = None
    distance_m: int | None = None
    # Straight from the newest fix to the target, in degrees clockwise from true north. Arrival is judged on this.
    bearing_deg: float | None = None
    # "north-east" and the like, for speech.
    compass: str | None = None
    arrived: bool = False
    # While a walking route is being followed: where to head now, the next route vertex a little way ahead, as
    # metres and degrees clockwise from true north from the newest fix. Without it the arrow uses the straight bearing.
    waypoint_distance_m: int | None = None
    waypoint_bearing_deg: float | None = None
    # The next turn on the route ("left", "slight_right", "uturn", "arrive" and so on) and the metres to it, and the
    # metres of walking left along the route.
    maneuver: str | None = None
    maneuver_distance_m: int | None = None
    route_remaining_m: int | None = None
    # Seconds since the backend received the newest fix, and what came with it.
    fix_age_s: float | None = None
    accuracy_m: float | None = None
    heading_deg: float | None = None
    heading_accuracy_deg: float | None = None
    course_deg: float | None = None
    speed_mps: float | None = None


class NavigateReply(BaseModel):
    target: NavStatus
    # Whether the target lies in an area the person has never walked; only set when new_area was asked for.
    new_area: bool | None = None
    # The other nearest matches, nearest first, without coordinates, so EDITH can mention them.
    others: list[dict]


class NoPlace(Exception):
    """Nothing matched near the person."""


class SearchFailed(Exception):
    """Every search service failed."""


class PlaceFinder(Protocol):
    async def find(self, lat: float, lon: float, request: NavigateRequest) -> list[Place]: ...


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
        places.append(Place(name=name, kind=_kind(tags), lat=plat, lon=plon, address=street_address(tags),
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
        places.append(Place(name=name, kind=kind, lat=plat, lon=plon, address=street_address(item.get("address")),
                            distance_m=round(distance_m(lat, lon, plat, plon))))
    return sorted(places, key=lambda p: p.distance_m)


def street_address(fields: dict | None) -> str | None:
    """The house number and street from OpenStreetMap address tags (addr:housenumber, addr:street) or a Nominatim
    address (house_number, road), or None without a street."""
    if not isinstance(fields, dict):
        return None
    street = fields.get("addr:street") or fields.get("road")
    if not street:
        return None
    number = fields.get("addr:housenumber") or fields.get("house_number")
    return clean_name(f"{number} {street}" if number else street)


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
    """Nominatim for a name, Overpass for a kind of place, and the places found remembered for a while."""

    def __init__(self, http: httpx.AsyncClient, sleep: Callable[[float], Awaitable] = asyncio.sleep,
                 clock: Callable[[], float] = time.monotonic) -> None:
        self._http, self._sleep, self._clock = http, sleep, clock
        self._nominatim_lock = asyncio.Lock()
        self._nominatim_last: float | None = None
        self._cache: OrderedDict[tuple, tuple[float, list[Place]]] = OrderedDict()

    async def find(self, lat: float, lon: float, request: NavigateRequest) -> list[Place]:
        lat, lon = round(lat, 4), round(lon, 4)
        name = (request.name or "").strip()
        key = (round(lat, 3), round(lon, 3), request.category, normalise(name))
        cached = self._cache.get(key)
        if cached is not None and self._clock() - cached[0] <= SEARCH_CACHE_S:
            self._cache.move_to_end(key)
            moved = [p.model_copy(update={"distance_m": round(distance_m(lat, lon, p.lat, p.lon))}) for p in cached[1]]
            return sorted(moved, key=lambda p: p.distance_m)
        found = await self._search(lat, lon, request.category, name)
        if found:
            self._cache[key] = (self._clock(), found)
            self._cache.move_to_end(key)
            while len(self._cache) > SEARCH_CACHE_MAX:
                self._cache.popitem(last=False)
        return found

    async def _search(self, lat: float, lon: float, category: str, name: str) -> list[Place]:
        """Raises SearchFailed only when no service answered at all: an empty answer is NoPlace, not a failure."""
        ends = self._clock() + NAVIGATE_TIMEOUT_S - SEARCH_MARGIN_S
        # Both start at once. The kind search nearby answers first in turn, since its places carry their real kind and
        # are truly the nearest; the name search returns only its ten best matches in about 5 km, and often stops
        # named after a building. When the kind search nearby misses a named place, the name search, usually done by
        # then, answers before any wider kind search is waited for.
        by_name = asyncio.create_task(self._nominatim(lat, lon, name)) if name else None
        near = asyncio.create_task(self._overpass(lat, lon, SEARCH_RADII_M[0], category)) if category != "any" else None
        answered = False
        try:
            if near:
                try:
                    found = await asyncio.wait_for(near, max(ends - self._clock(), 0.1))
                    answered = True
                    if name:
                        found = [p for p in found if matches_name(p, name)]
                    if found:
                        return dedupe(found)[:MAX_CHOICES]
                except TimeoutError:
                    log.info("the %d m search ran out of time", SEARCH_RADII_M[0])
                except (httpx.HTTPError, ValueError) as exc:
                    log.info("overpass search failed: %s", type(exc).__name__)
            loose: list[Place] = []
            if by_name:
                try:
                    found = await asyncio.wait_for(by_name, max(ends - self._clock(), 0.1))
                    answered = True
                    found = [p for p in found if category in TRANSIT or p.kind not in STOP_KINDS]
                    named = [p for p in found if matches_name(p, name)]
                    if named:
                        return dedupe(named)[:MAX_CHOICES]
                    # Not every word of the asked name is in these, as with a long official name; they are used only
                    # when the wider kind search finds nothing with the name either.
                    loose = found
                except TimeoutError:
                    log.info("the name search ran out of time")
                except (httpx.HTTPError, ValueError) as exc:
                    log.info("nominatim search failed: %s", type(exc).__name__)
            if near:
                for radius in SEARCH_RADII_M[1:]:
                    left = ends - self._clock()
                    if left < WIDE_MIN_S:
                        log.info("no time left for the %d m search", radius)
                        break
                    try:
                        found = await asyncio.wait_for(self._overpass(lat, lon, radius, category), left)
                    except TimeoutError:
                        log.info("the %d m search ran out of time", radius)
                        break
                    except (httpx.HTTPError, ValueError) as exc:
                        log.info("overpass search failed: %s", type(exc).__name__)
                        break
                    answered = True
                    if name:
                        found = [p for p in found if matches_name(p, name)]
                    if found:
                        return dedupe(found)[:MAX_CHOICES]
            if loose:
                return dedupe(loose)[:MAX_CHOICES]
            if not answered:
                raise SearchFailed
            return []
        finally:
            for task in (by_name, near):
                if task is not None and not task.done():
                    task.cancel()

    async def _overpass(self, lat: float, lon: float, radius: int, category: str) -> list[Place]:
        """Asks the first mirror, and each next one too whenever the ones asked so far are slow or fail. The first
        good answer wins and the rest are dropped."""
        query = overpass_query(lat, lon, radius, category)

        async def ask(url: str) -> list[Place]:
            response = await self._http.post(url, data={"data": query}, timeout=SERVICE_TIMEOUT_S,
                                             headers={"User-Agent": USER_AGENT})
            response.raise_for_status()
            reply = response.json()
            if isinstance(reply, dict) and reply.get("remark"):
                raise ValueError("Overpass reported a runtime error.")
            return overpass_places(reply, lat, lon)

        mirrors = iter(OVERPASS_URLS)
        pending = {asyncio.create_task(ask(next(mirrors)))}
        last: Exception | None = None
        try:
            while pending:
                done, pending = await asyncio.wait(pending, timeout=OVERPASS_HEDGE_S,
                                                   return_when=asyncio.FIRST_COMPLETED)
                for task in done:
                    try:
                        return task.result()
                    except (httpx.HTTPError, ValueError) as exc:
                        last = exc
                if (url := next(mirrors, None)) is not None:
                    pending.add(asyncio.create_task(ask(url)))
        finally:
            for task in pending:
                task.cancel()
        assert last is not None
        raise last

    async def address(self, lat: float, lon: float) -> str | None:
        """The street address at a place, from Nominatim's reverse lookup at building zoom."""
        async with self._nominatim_lock:
            if self._nominatim_last is not None and (wait := self._nominatim_last + NOMINATIM_GAP_S - self._clock()) > 0:
                await self._sleep(wait)
            try:
                response = await self._http.get(
                    NOMINATIM_REVERSE_URL, timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": USER_AGENT},
                    params={"lat": round(lat, 5), "lon": round(lon, 5), "format": "jsonv2", "zoom": 18,
                            "addressdetails": 1, "accept-language": "en"})
                response.raise_for_status()
                reply = response.json()
                return street_address(reply.get("address")) if isinstance(reply, dict) else None
            finally:
                self._nominatim_last = self._clock()

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
                            "addressdetails": 1, "accept-language": "en"})
                response.raise_for_status()
                return nominatim_places(response.json(), lat, lon)
            finally:
                self._nominatim_last = self._clock()


# A route asked for and failed is not asked for again for this long, and a route that arrived not again for the shorter
# gap, so a poll every second does not hammer the routing server.
ROUTE_RETRY_S, REROUTE_GAP_S = 10.0, 5.0


@dataclass
class _Target:
    place: Place
    set_at: float
    route: Route | None = None
    next_route_at: float = 0.0
    fetching: asyncio.Task | None = None
    adventure_id: UUID = field(default_factory=uuid4)


class Navigations:
    """Each profile's navigation target and the walking route to it, in memory like the positions it is measured from.
    Without a router the arrow keeps the straight bearing."""

    def __init__(self, clock: Callable[[], float] = time.time, router: Router | None = None) -> None:
        self._clock = clock
        self._router = router
        self._targets: dict[UUID, _Target] = {}
        self._tasks: set[asyncio.Task] = set()

    def set(self, profile_id: UUID, place: Place) -> None:
        self._targets[profile_id] = _Target(place, self._clock())
        log.info("navigating to %r (%s)", place.name, place.kind)

    def fill_address(self, profile_id: UUID, finder: object) -> None:
        """Looks up the target's street address in the background when the search gave none, so the reply to the
        search is not held up; the next poll carries it."""
        found = self._found(profile_id)
        lookup = getattr(finder, "address", None)
        if found is None or found.place.address or lookup is None:
            return

        async def fill() -> None:
            try:
                address = await asyncio.wait_for(lookup(found.place.lat, found.place.lon), ADDRESS_TIMEOUT_S)
            except (httpx.HTTPError, ValueError, TimeoutError) as exc:
                log.info("no address for %r: %s", found.place.name, type(exc).__name__)
                return
            if address and self._targets.get(profile_id) is found:
                found.place = found.place.model_copy(update={"address": address})

        task = asyncio.create_task(fill())
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    def clear(self, profile_id: UUID) -> bool:
        return self._targets.pop(profile_id, None) is not None

    def _found(self, profile_id: UUID) -> _Target | None:
        found = self._targets.get(profile_id)
        if found is None:
            return None
        if self._clock() - found.set_at > TARGET_LIFE_S:
            del self._targets[profile_id]
            return None
        return found

    def target(self, profile_id: UUID) -> Place | None:
        found = self._found(profile_id)
        return found.place if found else None

    def active(self, profile_id: UUID) -> bool:
        return self.target(profile_id) is not None

    def adventure(self, profile_id: UUID) -> UUID | None:
        """The id of the navigation under way, which names its trail."""
        found = self._found(profile_id)
        return found.adventure_id if found else None

    def guidance(self, profile_id: UUID, fix: LocationFix) -> Guidance | None:
        """Where the route says to head from `fix`, or None when there is no route that fits it. The first call, and a
        call more than OFF_ROUTE_M from the route, start fetching a route in the background, so a poll never waits on
        the routing server."""
        found = self._found(profile_id)
        if found is None:
            return None
        guided = guide(found.route, fix.lat, fix.lon) if found.route else None
        if self._router and (guided is None or guided.off_route) and found.fetching is None                 and self._clock() >= found.next_route_at:
            task = asyncio.create_task(self._fetch(found, fix.lat, fix.lon))
            found.fetching = task
            self._tasks.add(task)
            task.add_done_callback(self._tasks.discard)
        return None if guided is None or guided.off_route else guided

    async def _fetch(self, found: _Target, lat: float, lon: float) -> None:
        assert self._router is not None
        try:
            found.route = await asyncio.wait_for(self._router.route(lat, lon, found.place.lat, found.place.lon),
                                                 ROUTE_TIMEOUT_S)
            found.next_route_at = self._clock() + REROUTE_GAP_S
        except (RouteFailed, TimeoutError):
            found.route = None
            found.next_route_at = self._clock() + ROUTE_RETRY_S
        finally:
            found.fetching = None

    async def settle(self) -> None:
        """Waits for the routes being fetched; tests use it."""
        while self._tasks:
            await asyncio.gather(*self._tasks, return_exceptions=True)
            await asyncio.sleep(0)


def status(place: Place | None, latest: tuple[LocationFix, float] | None, guidance: Guidance | None = None) -> NavStatus:
    if place is None:
        # The newest reading still goes back, so the headset can learn north before directions start.
        if latest is None:
            return NavStatus(active=False)
        fix, age = latest
        return NavStatus(
            active=False, fix_age_s=round(age, 2), accuracy_m=fix.accuracy_m, heading_deg=fix.heading_deg,
            heading_accuracy_deg=fix.heading_accuracy_deg, course_deg=fix.course_deg, speed_mps=fix.speed_mps)
    if latest is None:
        return NavStatus(active=True, name=place.name, kind=place.kind, address=place.address)
    fix, age = latest
    distance = distance_m(fix.lat, fix.lon, place.lat, place.lon)
    bearing = bearing_deg(fix.lat, fix.lon, place.lat, place.lon)
    reply = NavStatus(
        active=True, name=place.name, kind=place.kind, address=place.address, distance_m=round(distance),
        bearing_deg=round(bearing, 1),
        compass=compass_word(bearing), arrived=arrived(distance, fix.accuracy_m), fix_age_s=round(age, 2),
        accuracy_m=fix.accuracy_m, heading_deg=fix.heading_deg, heading_accuracy_deg=fix.heading_accuracy_deg,
        course_deg=fix.course_deg, speed_mps=fix.speed_mps)
    if guidance:
        reply.waypoint_distance_m = round(guidance.waypoint_distance_m)
        reply.waypoint_bearing_deg = round(guidance.waypoint_bearing_deg, 1)
        reply.maneuver = guidance.turn
        reply.maneuver_distance_m = round(guidance.turn_distance_m)
        reply.route_remaining_m = round(guidance.remaining_m)
    return reply


def others(places: list[Place]) -> list[dict]:
    return [{"name": p.name, "kind": p.kind, "distance_m": p.distance_m} for p in places[1:]]
