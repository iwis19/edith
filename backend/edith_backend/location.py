"""Where a person is, as their phone reports it: the validated fix, the place name and sights resolved from it, and the
in-memory store that keeps both for six hours. Nothing here touches the database, and coordinates are never logged."""
import asyncio
import logging
import math
import time
from collections.abc import Callable
from datetime import datetime
from typing import Protocol
from uuid import UUID

from pydantic import BaseModel, Field

log = logging.getLogger("edith.location")

KEEP_S = 6 * 3600
# GET /location reports a position only this long after the phone last sent one, so the headset sees a closed phone
# page as gone. The phone page re-sends every 10 s, so 30 s is three missed re-sends.
PHONE_SILENT_S = 30.0
# A prompt trusts a position this long after the phone last sent one. It outlasts PHONE_SILENT_S: a person who
# stopped at a place is still there minutes later, and a phone that dropped out for a minute is not a reason to
# forget where the Look is happening.
FRESH_S = 30 * 60
# A new fix re-resolves only when it moved this far from the last resolved one, or that result is this old.
MOVE_M = 75.0
RESOLVE_MAX_AGE_S = 600.0
# A resolve that lost a service is retried on a later fix, but not on every one.
RETRY_S = 60.0
NEAREST_SHOWN = 5


class LocationFix(BaseModel):
    lat: float = Field(ge=-90, le=90)
    lon: float = Field(ge=-180, le=180)
    accuracy_m: float = Field(ge=0, le=5000)
    taken_at: datetime
    # Where the phone's top points, in degrees clockwise from north, from its compass. Navigation uses it to line the
    # headset's own yaw up with north; nothing else reads it.
    heading_deg: float | None = Field(None, ge=0, le=360)
    # The compass's own estimate of its error, when the browser gives one.
    heading_accuracy_deg: float | None = Field(None, ge=0, le=360)
    # The direction of travel from GPS, in degrees clockwise from north, and the speed. Browsers give these only while
    # the phone is moving.
    course_deg: float | None = Field(None, ge=0, le=360)
    speed_mps: float | None = Field(None, ge=0, le=100)


class Sight(BaseModel):
    name: str
    kind: str
    distance_m: int
    # How much an explorer would want it named, best first: 0 a landmark, attraction or anything linked to Wikidata or
    # Wikipedia, 1 a university or public building, 2 another named building, 3 an ordinary shop or restaurant. The
    # nearest of the best tiers fill the list.
    tier: int = 2
    # The article tag OpenStreetMap carries for it ("en:Yasaka Pagoda") and, once fetched, the opening of that article.
    wikipedia: str | None = None
    fact: str | None = None


class Whereabouts(BaseModel):
    place: str | None
    sights: list[Sight]
    # The phone's position the place was resolved for, rounded to about 10 m.
    lat: float | None = None
    lon: float | None = None


class LocationSummary(BaseModel):
    """What the phone and the headset read back: the latest resolved whereabouts, which may be from an earlier fix."""
    place: str | None
    sights_count: int
    nearest: list[str]
    resolving: bool
    # True while the headset is guiding the person somewhere, so the phone sends its position and heading more often.
    navigating: bool = False


class Resolution(BaseModel):
    """What the resolver returns: `complete` is False when a service failed, so a later fix retries it."""
    whereabouts: Whereabouts
    complete: bool


class Resolver(Protocol):
    async def resolve(self, fix: LocationFix) -> Resolution | None: ...


def distance_m(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    p1, p2 = math.radians(lat1), math.radians(lat2)
    a = math.sin((p2 - p1) / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(math.radians(lon2 - lon1) / 2) ** 2
    return 6371000 * 2 * math.asin(math.sqrt(a))


class _Entry:
    def __init__(self, fix: LocationFix, now: float) -> None:
        self.fix, self.stored_at = fix, now
        self.whereabouts: Whereabouts | None = None
        self.resolved_fix: LocationFix | None = None
        self.resolved_at = 0.0
        self.complete = False
        self.resolving = False


def _summarise(entry: _Entry) -> LocationSummary:
    found = entry.whereabouts
    return LocationSummary(
        place=found.place if found else None,
        sights_count=len(found.sights) if found else 0,
        nearest=[s.name for s in found.sights[:NEAREST_SHOWN]] if found else [],
        resolving=entry.resolving,
    )


class Locations:
    """Each profile's latest fix and its resolved whereabouts. `resolver` runs in a background task per fix, never on
    a request that needs the answer."""

    def __init__(self, resolver: Resolver, clock: Callable[[], float] = time.time) -> None:
        self._resolver, self._clock = resolver, clock
        self._entries: dict[UUID, _Entry] = {}
        self._tasks: set[asyncio.Task] = set()

    def _entry(self, profile_id: UUID, max_age_s: float = KEEP_S) -> _Entry | None:
        entry = self._entries.get(profile_id)
        if entry is None:
            return None
        age = self._clock() - entry.stored_at
        if age > KEEP_S:
            del self._entries[profile_id]
            return None
        return entry if age <= max_age_s else None

    def has_fix(self, profile_id: UUID) -> bool:
        return self._entry(profile_id) is not None

    def latest(self, profile_id: UUID, max_age_s: float = FRESH_S) -> tuple[LocationFix, float] | None:
        """The newest fix and how many seconds ago it arrived, when that is within `max_age_s`. Navigation reads this;
        prompts never see the coordinates."""
        entry = self._entry(profile_id, max_age_s)
        return (entry.fix, self._clock() - entry.stored_at) if entry else None

    def whereabouts(self, profile_id: UUID, max_age_s: float = FRESH_S) -> Whereabouts | None:
        """The resolved place and sights, when the phone sent a position within `max_age_s`. The Look, Ask and Scan
        read this."""
        entry = self._entry(profile_id, max_age_s)
        return entry.whereabouts if entry else None

    def update(self, profile_id: UUID, fix: LocationFix) -> LocationSummary:
        now = self._clock()
        self._sweep()
        entry = self._entry(profile_id) or _Entry(fix, now)
        entry.fix, entry.stored_at = fix, now
        self._entries[profile_id] = entry
        if not entry.resolving and self._needs_resolving(entry, fix):
            entry.resolving = True
            task = asyncio.create_task(self._resolve(entry, fix))
            self._tasks.add(task)
            task.add_done_callback(self._tasks.discard)
        return _summarise(entry)

    def summary(self, profile_id: UUID, max_age_s: float = PHONE_SILENT_S) -> LocationSummary | None:
        """What the headset reads, or None once the phone has sent nothing for `max_age_s`."""
        entry = self._entry(profile_id, max_age_s)
        return _summarise(entry) if entry else None

    async def settle(self) -> None:
        """Waits for the resolves in flight; tests use it."""
        while self._tasks:
            await asyncio.gather(*self._tasks, return_exceptions=True)
            # A finished task leaves the set in a done callback, which runs only once the loop gets a turn.
            await asyncio.sleep(0)

    def _sweep(self) -> None:
        now = self._clock()
        for profile_id in [p for p, e in self._entries.items() if now - e.stored_at > KEEP_S]:
            del self._entries[profile_id]

    def _needs_resolving(self, entry: _Entry, fix: LocationFix) -> bool:
        if entry.resolved_fix is None:
            return True
        age = self._clock() - entry.resolved_at
        moved = distance_m(fix.lat, fix.lon, entry.resolved_fix.lat, entry.resolved_fix.lon)
        return moved > MOVE_M or age > RESOLVE_MAX_AGE_S or (not entry.complete and age > RETRY_S)

    async def _resolve(self, entry: _Entry, fix: LocationFix) -> None:
        try:
            resolution = await self._resolver.resolve(fix)
            if resolution is not None:
                entry.whereabouts, entry.resolved_fix = resolution.whereabouts, fix
                entry.resolved_at, entry.complete = self._clock(), resolution.complete
                log.info("resolved a fix: %r, %d sights%s", resolution.whereabouts.place,
                         len(resolution.whereabouts.sights), "" if resolution.complete else " (partial)")
        except Exception as exc:
            log.warning("could not resolve a fix: %s", type(exc).__name__)
        finally:
            entry.resolving = False


def describe_place(found: Whereabouts | None, max_sights: int) -> str | None:
    """The facts a prompt is given about where the person is, or None when nothing is known."""
    if found is None or not found.place and not found.sights:
        return None
    head = f"This person is at {found.place}." if found.place else "This person's place name is not known."
    if found.lat is not None and found.lon is not None:
        head += f" Their phone puts them at latitude {found.lat:.4f}, longitude {found.lon:.4f}."
    if not found.sights:
        return f"{head} No named sights are known nearby."
    # The sights with a fact are listed first, so a far landmark's fact is not cut off by a nearer plain building.
    by_fact = [s for s in found.sights if s.fact] + [s for s in found.sights if not s.fact]
    shown = sorted(by_fact[:max_sights], key=lambda s: s.distance_m)
    lines = ", ".join(f"{s.name} ({s.distance_m} m, {s.kind})" for s in shown)
    text = f"{head} Named sights nearby, nearest first: {lines}."
    if facts := [f"- {s.name}: {s.fact}" for s in shown if s.fact]:
        text += "\nWhat Wikipedia says about some of them:\n" + "\n".join(facts)
    return text
