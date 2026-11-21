"""Bounty: each profile's active scavenger hunt. The backend holds the hunt, so the headset never sends targets. Looks
tick targets off through `Hunts.tick`; the hunt lives in memory and is written through to the store."""
import asyncio
import logging
import math
import re
from collections.abc import Awaitable, Callable, Collection, Sequence
from dataclasses import dataclass, replace
from datetime import UTC, datetime, timedelta
from typing import Literal, Protocol, get_args
from uuid import UUID, uuid4

from .events import HuntState, Rarity, TargetState
from .location import Whereabouts, describe_place

log = logging.getLogger("edith.hunt")

TARGET_COUNT = 5
# A rarity target never asks for more than this tier.
MAX_TARGET_TIER: Rarity = "rare"
# A Look whose name starts with "Maybe", or whose confidence is below this, ticks nothing.
MIN_TICK_CONFIDENCE = 0.5
PROMPT_SIGHTS = 5

EndReason = Literal["found_all", "stopped"]
Status = Literal["active", "found_all", "stopped", "timeout"]

_TIER_RANK = {tier: rank for rank, tier in enumerate(get_args(Rarity))}


@dataclass(frozen=True)
class Target:
    index: int
    text: str
    # Set only for a rarity target, which a Look of this tier or better ticks.
    tier: Rarity | None = None
    found_at: datetime | None = None
    # The Look that found it. It has no foreign key: the Look row is written after its stream ends.
    look_ref: UUID | None = None


@dataclass(frozen=True)
class Hunt:
    hunt_ref: UUID
    profile_id: UUID
    started_at: datetime
    ends_at: datetime
    targets: tuple[Target, ...]
    place: str | None = None
    ended_at: datetime | None = None
    end_reason: EndReason | None = None

    def status(self, now: datetime) -> Status:
        """A timeout is derived from the clock, never stored, so it needs no writer."""
        if self.end_reason is not None:
            return self.end_reason
        return "timeout" if now >= self.ends_at else "active"

    @property
    def found(self) -> int:
        return sum(t.found_at is not None for t in self.targets)


@dataclass(frozen=True)
class StoredHunt:
    hunt: Hunt
    # Per target index: the memory collected from the Look that found it, None when not collected.
    memory_ids: tuple[int | None, ...]


@dataclass(frozen=True)
class TickResult:
    hunt: Hunt
    target: Target
    status: Literal["active", "found_all"]


class HuntStore(Protocol):
    async def save_hunt(self, hunt: Hunt) -> None: ...
    async def save_tick(self, hunt_ref: UUID, target: Target) -> None: ...
    async def end_hunt(self, hunt_ref: UUID, ended_at: datetime, reason: EndReason) -> None: ...
    async def load_active_hunts(self) -> list[Hunt]: ...
    async def list_hunts(self, profile_id: UUID, limit: int) -> list[StoredHunt]: ...


# Writes the targets for a place description and the person's interests. Never raises, always returns TARGET_COUNT.
TargetDrafter = Callable[[str | None, Sequence[str]], Awaitable[Sequence[Target]]]


def fits_tier(target: Target, tier: Rarity | None) -> bool:
    return target.tier is not None and tier is not None and _TIER_RANK[tier] >= _TIER_RANK[target.tier]


def can_tick(name: str, confidence: float) -> bool:
    return not name.strip().lower().startswith("maybe") and confidence >= MIN_TICK_CONFIDENCE


def numbered(targets: Sequence[tuple[int, str]]) -> str:
    """The open targets as a prompt lists them: the number is the target's index plus one."""
    return "\n".join(f"{index + 1}. {text}" for index, text in targets)


def parse_number(value: object, open_indexes: Collection[int]) -> int | None:
    """The target index for the number a model wrote from a `numbered` list, as an int or as text that starts with
    digits. None for "none" or any number that names no open target."""
    if isinstance(value, bool) or not isinstance(value, (int, str)):
        return None
    match = re.match(r"\s*(\d{1,3})\b", str(value))
    index = int(match.group(1)) - 1 if match else -1
    return index if index in open_indexes else None


class Hunts:
    """Each profile's active hunt. A single replica, like Locations: the in-memory hunt is the truth while it runs,
    and the store keeps the record. `clock` is injectable so tests can move time."""

    def __init__(
        self,
        store: HuntStore,
        draft: TargetDrafter,
        whereabouts: Callable[[UUID], Whereabouts | None] = lambda _: None,
        duration_s: float = 300.0,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        self._store, self._draft, self._whereabouts = store, draft, whereabouts
        self._duration, self._clock = timedelta(seconds=duration_s), clock
        self._active: dict[UUID, Hunt] = {}
        self._writes: set[asyncio.Task] = set()
        self._tails: dict[UUID, asyncio.Task] = {}

    def now(self) -> datetime:
        return self._clock()

    async def restore(self) -> None:
        """Takes back the hunts still running after a restart, as at startup."""
        now = self._clock()
        for hunt in await self._store.load_active_hunts():
            current = self._active.get(hunt.profile_id)
            if hunt.status(now) == "active" and (current is None or current.started_at < hunt.started_at):
                self._active[hunt.profile_id] = hunt

    async def start(self, profile_id: UUID, interests: Sequence[str] = ()) -> Hunt:
        """Starts a hunt; one still running for the profile ends as stopped."""
        found = self._whereabouts(profile_id)
        targets = await self._draft(describe_place(found, PROMPT_SIGHTS), interests)
        now = self._clock()
        hunt = Hunt(hunt_ref=uuid4(), profile_id=profile_id, started_at=now, ends_at=now + self._duration,
                    targets=tuple(targets), place=found.place if found else None)
        self._end(profile_id, "stopped", now)
        self._active[profile_id] = hunt
        self._persist(hunt.hunt_ref, lambda: self._store.save_hunt(hunt))
        return hunt

    def current(self, profile_id: UUID) -> Hunt | None:
        hunt = self._active.get(profile_id)
        return hunt if hunt is not None and hunt.status(self._clock()) == "active" else None

    def open_targets(self, profile_id: UUID) -> list[tuple[int, str]] | None:
        """(index, text) of each target still open, or None when there is no active hunt."""
        hunt = self.current(profile_id)
        return None if hunt is None else [(t.index, t.text) for t in hunt.targets if t.found_at is None]

    def stop(self, profile_id: UUID) -> Hunt | None:
        return self._end(profile_id, "stopped", self._clock())

    def tick(self, profile_id: UUID, look_ref: UUID, pick: int | None, tier: Rarity | None) -> TickResult | None:
        """Ticks off at most one open target for a Look: the theme target the card call picked, else the lowest rarity
        target the Look's tier meets. The caller has already dropped Looks too unsure to count."""
        hunt = self.current(profile_id)
        if hunt is None:
            return None
        target = self._match(hunt, pick, tier)
        if target is None:
            return None
        now = self._clock()
        found = replace(target, found_at=now, look_ref=look_ref)
        targets = tuple(found if t.index == found.index else t for t in hunt.targets)
        hunt = replace(hunt, targets=targets)
        self._persist(hunt.hunt_ref, lambda: self._store.save_tick(hunt.hunt_ref, found))
        if hunt.found == len(targets):
            self._active.pop(profile_id, None)
            return TickResult(self._end_hunt(hunt, "found_all", now), found, "found_all")
        self._active[profile_id] = hunt
        return TickResult(hunt, found, "active")

    async def history(self, profile_id: UUID, limit: int) -> list[StoredHunt]:
        """Past and current hunts, newest first. Raises StoreUnavailable."""
        await self.settle()
        return await self._store.list_hunts(profile_id, limit)

    async def settle(self) -> None:
        """Waits for the writes in flight."""
        while self._writes:
            await asyncio.wait(set(self._writes))

    @staticmethod
    def _match(hunt: Hunt, pick: int | None, tier: Rarity | None) -> Target | None:
        picked = next((t for t in hunt.targets if t.index == pick), None)
        if picked is not None and picked.found_at is None and picked.tier is None:
            return picked
        return next((t for t in hunt.targets if t.found_at is None and fits_tier(t, tier)), None)

    def _end(self, profile_id: UUID, reason: EndReason, now: datetime) -> Hunt | None:
        hunt = self.current(profile_id)
        self._active.pop(profile_id, None)
        if hunt is None:
            return None
        return self._end_hunt(hunt, reason, now)

    def _end_hunt(self, hunt: Hunt, reason: EndReason, now: datetime) -> Hunt:
        self._persist(hunt.hunt_ref, lambda: self._store.end_hunt(hunt.hunt_ref, now, reason))
        return replace(hunt, ended_at=now, end_reason=reason)

    def _persist(self, hunt_ref: UUID, write: Callable[[], Awaitable[None]]) -> None:
        """Runs a write in the background, after the earlier writes for the same hunt, so a Look never waits on the
        database."""
        previous = self._tails.get(hunt_ref)

        async def run() -> None:
            if previous is not None:
                await asyncio.wait({previous})
            try:
                await write()
            except Exception:
                log.exception("a hunt write failed")

        task = asyncio.create_task(run())
        self._tails[hunt_ref] = task
        self._writes.add(task)

        def done(finished: asyncio.Task) -> None:
            self._writes.discard(finished)
            if self._tails.get(hunt_ref) is finished:
                del self._tails[hunt_ref]

        task.add_done_callback(done)


def to_state(hunt: Hunt, now: datetime, memory_ids: Sequence[int | None] = ()) -> HuntState:
    status = hunt.status(now)
    remaining = math.ceil((hunt.ends_at - now).total_seconds()) if status == "active" else 0
    targets = [
        TargetState(index=t.index, text=t.text, tier=t.tier, found=t.found_at is not None, found_at=t.found_at,
                    look_ref=t.look_ref.hex if t.look_ref else None,
                    memory_id=memory_ids[i] if i < len(memory_ids) else None)
        for i, t in enumerate(hunt.targets)
    ]
    return HuntState(hunt_ref=hunt.hunt_ref.hex, status=status, started_at=hunt.started_at, ends_at=hunt.ends_at,
                     ended_at=hunt.ended_at, remaining_s=max(remaining, 0), place=hunt.place, found=hunt.found,
                     targets=targets)
