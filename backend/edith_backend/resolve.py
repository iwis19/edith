"""Turns a fix into a place name and the named sights near it, and a place name into its centre, from OpenStreetMap
Nominatim and Overpass. Coordinates leave the backend rounded to 4 decimals (about 11 m), and the User-Agent names the
app and its version only."""
import asyncio
import logging
import re
import time
from urllib.parse import quote
from collections import OrderedDict
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from typing import Protocol

import httpx

from . import VERSION
from .location import LocationFix, Resolution, Sight, Whereabouts, distance_m

log = logging.getLogger("edith.location")

USER_AGENT = f"EDITH/{VERSION}"
# Wikimedia refuses a User-Agent that gives no way to reach the project, so its requests name the public repository.
WIKIPEDIA_USER_AGENT = f"{USER_AGENT} (+https://github.com/iwis19/westernhackers)"
NOMINATIM_URL = "https://nominatim.openstreetmap.org/reverse"
NOMINATIM_SEARCH_URL = "https://nominatim.openstreetmap.org/search"
# The public Overpass servers are often at capacity, so the next answers when one fails.
OVERPASS_URLS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.openstreetmap.fr/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
)
OVERPASS_RADIUS_M = 300
MAX_SIGHTS = 25
# A campus-sized relation matched by `around` can have its centre kilometres away, so anything past this is not near
# enough to name.
MAX_SIGHT_M = 600
SERVICE_TIMEOUT_S = 5.0
# Nominatim's usage policy allows one request a second.
NOMINATIM_GAP_S = 1.0
# Fixes that round to the same point share one answer, so a person standing still or several profiles in one room cost
# the services a single lookup.
CACHE_TTL_S = 30 * 60
CACHE_MAX = 256
# Places are few and their centres do not move, so a searched place is kept until the process ends, up to this many.
PLACE_CACHE_MAX = 1024
# Names come from OpenStreetMap, which anyone can edit, and go into prompts.
MAX_NAME_CHARS = 80
# The opening of a sight's Wikipedia article is fetched for the nearest few sights that carry one, once per article.
FACT_SIGHTS = 6
FACT_CHARS = 400
FACT_CACHE_MAX = 512
WIKI_LANG = re.compile(r"^[a-z]{2,3}(-[a-z]{2,8})?$")

# Nominatim reverse at street zoom names the road, not the campus around it, so a notable unit usually comes from the
# areas Overpass says contain the point. Their tags, most notable first.
CONTAINING_AREAS = (
    ("amenity", "university|college"),
    ("leisure", "park|nature_reserve|garden"),
    ("tourism", "attraction|zoo|theme_park"),
)
NOTABLE_KEYS = ("university", "college", "tourism", "attraction", "park", "leisure", "historic")
NOTABLE_TYPES = {"university", "college", "park", "attraction", "museum", "garden", "nature_reserve", "zoo", "theme_park"}
AREA_KEYS = ("neighbourhood", "suburb", "quarter", "city_district")
CITY_KEYS = ("city", "town", "village", "municipality", "hamlet")


def overpass_query(lat: float, lon: float) -> str:
    around = f"(around:{OVERPASS_RADIUS_M},{lat},{lon})"
    containing = "".join(f'area.a["name"]["{key}"~"^({values})$"];' for key, values in CONTAINING_AREAS)
    return (
        f"[out:json][timeout:5];is_in({lat},{lon})->.a;({containing});out tags;("
        f'nwr{around}["name"]["building"];'
        f'nwr{around}["name"]["historic"];'
        f'nwr{around}["name"]["tourism"~"^(attraction|artwork|museum|viewpoint)$"];'
        f'nwr{around}["name"]["amenity"="place_of_worship"];'
        f'nwr{around}["name"]["man_made"="tower"];'
        f'nwr{around}["name"]["wikidata"];'
        f'nwr{around}["name"]["wikipedia"];'
        ");out center tags;"
    )


def clean_name(name: str) -> str:
    return " ".join(name.split())[:MAX_NAME_CHARS]


def containing_name(reply: dict) -> str | None:
    """The most notable named area that contains the point: a university, else a park, else an attraction. Overpass
    returns those areas as their original ways and relations, told apart from the sights by having no geometry."""
    best: tuple[int, str] | None = None
    for element in reply.get("elements", []):
        tags = element.get("tags") or {}
        for rank, (key, values) in enumerate(CONTAINING_AREAS):
            if "center" not in element and "lat" not in element and tags.get("name") and tags.get(key) in values.split("|"):
                if best is None or rank < best[0]:
                    best = (rank, clean_name(tags["name"]))
    return best[1] if best else None


def place_name(reply: dict, notable_area: str | None = None) -> str | None:
    """The most specific notable unit (a university, campus, park or attraction), else the neighbourhood, then the
    city, region and country."""
    address = reply.get("address") or {}
    notable = notable_area or next((address[k] for k in NOTABLE_KEYS if address.get(k)), None)
    if notable is None and reply.get("type") in NOTABLE_TYPES and reply.get("name"):
        notable = reply["name"]
    area = next((address[k] for k in AREA_KEYS if address.get(k)), None)
    city = next((address[k] for k in CITY_KEYS if address.get(k)), None)
    parts: list[str] = []
    for part in (notable or area, city, address.get("state"), address.get("country")):
        if part and clean_name(part) not in parts:
            parts.append(clean_name(part))
    return ", ".join(parts) or None


def _kind(tags: dict) -> str:
    if tags.get("tourism"):
        return tags["tourism"]
    if tags.get("historic"):
        return "historic" if tags["historic"] == "yes" else f"historic {tags['historic'].replace('_', ' ')}"
    if tags.get("amenity") == "place_of_worship":
        return "place of worship"
    if tags.get("man_made") == "tower":
        return "tower"
    building = tags.get("building", "yes")
    return "building" if building == "yes" else f"{building.replace('_', ' ')} building"


# Buildings that are part of a campus or serve the public, worth naming ahead of homes and offices.
PUBLIC_BUILDINGS = {"university", "college", "school", "public", "civic", "government", "library", "museum", "church",
                    "cathedral", "chapel", "mosque", "temple", "synagogue", "train_station", "stadium", "hospital"}
PUBLIC_AMENITIES = {"university", "college", "library", "theatre", "arts_centre", "townhall", "concert_hall", "cinema"}
COMMERCIAL_BUILDINGS = {"retail", "commercial", "supermarket", "kiosk", "shop"}
EATERIES = {"restaurant", "cafe", "fast_food", "bar", "pub", "food_court", "ice_cream"}


def tier(tags: dict) -> int:
    """See Sight.tier. A chain's branch carries only brand:wikidata, so it is not lifted into the top tier."""
    if (tags.get("tourism") or tags.get("historic") or tags.get("man_made") == "tower"
            or tags.get("amenity") == "place_of_worship" or tags.get("wikidata") or tags.get("wikipedia")):
        return 0
    if tags.get("building") in PUBLIC_BUILDINGS or tags.get("amenity") in PUBLIC_AMENITIES:
        return 1
    if tags.get("shop") or tags.get("amenity") in EATERIES or tags.get("building") in COMMERCIAL_BUILDINGS:
        return 3
    return 2


def overpass_sights(reply: dict, lat: float, lon: float) -> list[Sight]:
    sights = []
    for element in reply.get("elements", []):
        tags = element.get("tags") or {}
        point = element if "lat" in element else element.get("center") or {}
        name = clean_name(tags.get("name") or "")
        if not name or "lat" not in point:
            continue
        sights.append(Sight(name=name, kind=_kind(tags), tier=tier(tags), wikipedia=tags.get("wikipedia"),
                            distance_m=round(distance_m(lat, lon, point["lat"], point["lon"]))))
    return sights


class PlaceLocator(Protocol):
    async def locate(self, place: str) -> tuple[float, float] | None: ...


def normalise(name: str) -> str:
    words = re.sub(r"[^\w\s]", " ", re.sub(r"['’]", "", name.casefold())).split()
    return " ".join(words[1:] if words[:1] == ["the"] else words)


def merge_sights(sights: list[Sight]) -> list[Sight]:
    """The best tiers fill the list, nearest first within each, and the kept ones are then given nearest first. A
    duplicate name keeps its best tier."""
    seen: dict[str, Sight] = {}
    for sight in sights:
        key = normalise(sight.name)
        if key not in seen or sight.tier < seen[key].tier:
            seen[key] = sight
    kept = sorted(seen.values(), key=lambda s: (s.tier, s.distance_m))[:MAX_SIGHTS]
    return sorted(kept, key=lambda s: s.distance_m)


def search_point(reply: object) -> tuple[float, float] | None:
    """The first result's position from a Nominatim search reply, None when it found nothing."""
    if not isinstance(reply, list):
        raise ValueError("A Nominatim search reply must be a list.")
    if not reply:
        return None
    lat, lon = float(reply[0]["lat"]), float(reply[0]["lon"])
    if not (-90 <= lat <= 90 and -180 <= lon <= 180):
        raise ValueError("A Nominatim search result is off the globe.")
    return lat, lon


@dataclass
class _Osm:
    sights: list[Sight] = field(default_factory=list)
    campus: str | None = None


class HttpResolver:
    def __init__(self, http: httpx.AsyncClient, sleep: Callable[[float], Awaitable] = asyncio.sleep,
                 clock: Callable[[], float] = time.monotonic, nominatim_gap_s: float = NOMINATIM_GAP_S) -> None:
        self._http = http
        self._sleep, self._clock, self._gap = sleep, clock, nominatim_gap_s
        self._nominatim_lock = asyncio.Lock()
        self._nominatim_last: float | None = None
        self._cache: OrderedDict[tuple[float, float], tuple[float, Resolution]] = OrderedDict()
        self._points: OrderedDict[str, tuple[float, float] | None] = OrderedDict()
        self._facts: OrderedDict[str, str | None] = OrderedDict()

    async def resolve(self, fix: LocationFix) -> Resolution | None:
        lat, lon = round(fix.lat, 4), round(fix.lon, 4)
        if (cached := self._cached(lat, lon)) is not None:
            return cached
        address, osm = await asyncio.gather(self._reverse(lat, lon), self._overpass(lat, lon), return_exceptions=True)
        failed = [type(r).__name__ for r in (address, osm) if isinstance(r, BaseException)]
        if len(failed) == 2:
            log.warning("every location service failed: %s", failed)
            return None
        if failed:
            log.info("a location service failed: %s", failed)
        osm = _Osm() if isinstance(osm, BaseException) else osm
        place = place_name({} if isinstance(address, BaseException) else address, osm.campus)
        # The campus is already the place, so it is not also a sight, unless it has an article to take a fact from.
        campus = normalise(osm.campus) if osm.campus else None
        near = [s for s in osm.sights if s.distance_m <= MAX_SIGHT_M and (normalise(s.name) != campus or s.wikipedia)]
        sights = await self._with_facts(merge_sights(near))
        resolution = Resolution(whereabouts=Whereabouts(place=place, sights=sights, lat=lat, lon=lon), complete=not failed)
        if resolution.complete:
            self._remember(lat, lon, resolution)
        return resolution

    async def _with_facts(self, sights: list[Sight]) -> list[Sight]:
        """The nearest sights that have a Wikipedia article, each given the opening of it. A fetch that fails leaves
        that sight without a fact."""
        wanted = [s for s in sights if s.wikipedia][:FACT_SIGHTS]
        facts = await asyncio.gather(*(self._fact(s.wikipedia) for s in wanted), return_exceptions=True)
        found = {s.name: f for s, f in zip(wanted, facts) if isinstance(f, str)}
        return [s.model_copy(update={"fact": found[s.name]}) if s.name in found else s for s in sights]

    async def _fact(self, article: str) -> str | None:
        if article in self._facts:
            return self._facts[article]
        lang, _, title = article.partition(":")
        if not WIKI_LANG.match(lang) or not title:
            return None
        try:
            response = await self._http.get(
                f"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{quote(title.replace(' ', '_'), safe='')}",
                timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": WIKIPEDIA_USER_AGENT}, follow_redirects=True)
            response.raise_for_status()
            extract = response.json().get("extract")
        except (httpx.HTTPError, ValueError) as exc:
            log.info("a Wikipedia summary was not fetched: %s", type(exc).__name__)
            return None
        fact = " ".join(extract.split())[:FACT_CHARS] if isinstance(extract, str) and extract.strip() else None
        self._facts[article] = fact
        while len(self._facts) > FACT_CACHE_MAX:
            self._facts.popitem(last=False)
        return fact

    def _cached(self, lat: float, lon: float) -> Resolution | None:
        found = self._cache.get((lat, lon))
        if found is None:
            return None
        if self._clock() - found[0] > CACHE_TTL_S:
            del self._cache[(lat, lon)]
            return None
        return found[1]

    def _remember(self, lat: float, lon: float, resolution: Resolution) -> None:
        self._cache[(lat, lon)] = (self._clock(), resolution)
        while len(self._cache) > CACHE_MAX:
            self._cache.popitem(last=False)

    async def _reverse(self, lat: float, lon: float) -> dict:
        return await self._nominatim(NOMINATIM_URL, {
            "format": "jsonv2", "lat": lat, "lon": lon, "zoom": 17, "addressdetails": 1, "accept-language": "en"})

    async def locate(self, place: str) -> tuple[float, float] | None:
        """The centre of a place by its name, as (lat, lon), or None when Nominatim does not know it. A failed lookup
        raises and is not remembered. Only the place text is sent."""
        key = normalise(place)
        if not key:
            return None
        if key in self._points:
            self._points.move_to_end(key)
            return self._points[key]
        point = search_point(await self._nominatim(
            NOMINATIM_SEARCH_URL, {"format": "jsonv2", "limit": 1, "q": place, "accept-language": "en"}))
        self._points[key] = point
        while len(self._points) > PLACE_CACHE_MAX:
            self._points.popitem(last=False)
        return point

    async def _nominatim(self, url: str, params: dict):
        async with self._nominatim_lock:
            if self._nominatim_last is not None:
                wait = self._nominatim_last + self._gap - self._clock()
                if wait > 0:
                    await self._sleep(wait)
            try:
                response = await self._http.get(
                    url, timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": USER_AGENT}, params=params)
                response.raise_for_status()
                return response.json()
            finally:
                self._nominatim_last = self._clock()

    async def _overpass(self, lat: float, lon: float) -> _Osm:
        last: Exception | None = None
        for url in OVERPASS_URLS:
            try:
                response = await self._http.post(url, data={"data": overpass_query(lat, lon)},
                                                 timeout=SERVICE_TIMEOUT_S, headers={"User-Agent": USER_AGENT})
                response.raise_for_status()
                reply = response.json()
                # A query that ran out of time or memory still answers 200, with a remark and few or no elements.
                if isinstance(reply, dict) and reply.get("remark"):
                    raise ValueError("Overpass reported a runtime error.")
                return _Osm(overpass_sights(reply, lat, lon), containing_name(reply))
            except (httpx.HTTPError, ValueError) as exc:
                last = exc
        assert last is not None
        raise last
