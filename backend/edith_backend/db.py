import asyncio
import json
import logging
import sys
import time
from array import array
from dataclasses import dataclass
from datetime import datetime
from typing import Protocol
from uuid import UUID

from psycopg.types.json import Jsonb
from psycopg_pool import AsyncConnectionPool

from .hunts import EndReason, Hunt, StoredHunt, Target

log = logging.getLogger("edith.db")

_STATUS_TTL_S = 5.0


@dataclass(frozen=True)
class LookRecord:
    look_ref: UUID
    profile_id: UUID
    client_look_id: int
    source: str
    captured_at: datetime
    width: int
    height: int
    status: str
    name: str | None
    confidence: float | None
    fact: str | None
    fact_sources: list[dict]
    rarity: str | None
    duration_ms: int
    kind: str | None = None
    place: str | None = None
    photo_place: str | None = None


@dataclass(frozen=True)
class Memory:
    id: int
    look_ref: UUID
    name: str
    kind: str | None
    fact: str | None
    sources: list[dict]
    rarity: str | None
    place: str | None
    created_at: datetime


@dataclass(frozen=True)
class ToldFact:
    """A fact a profile was told, the kind of object it was about, and that kind's embedding when there was one.
    `told_at` is epoch seconds."""
    kind: str
    fact: str
    embedding: array | None
    told_at: float


# How many told facts per profile are kept, in memory and in the table.
TOLD_FACTS_KEPT = 50
# How far back the places photos showed are loaded at startup, the card agent's place window.
PLACE_WINDOW_HOURS = 6


class StoreUnavailable(Exception):
    """The database is not configured or the query failed."""


class Store(Protocol):
    async def open(self) -> None: ...
    async def close(self) -> None: ...
    async def status(self) -> str: ...
    async def record_look(self, record: LookRecord) -> None: ...
    async def collect(
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_at: datetime | None = None
    ) -> int | None:
        """Saves the finished Look as a memory, once per Look, and returns the memory's id. None when the profile has
        no finished Look with that ref. Raises StoreUnavailable."""
        ...
    async def list_memories(self, profile_id: UUID, limit: int) -> list[Memory]: ...
    async def memory_image(self, profile_id: UUID, memory_id: int) -> bytes | None: ...
    async def load_told_facts(self, profile_id: UUID) -> list[ToldFact]:
        """The profile's newest told facts, oldest first. Empty when there are none or the query failed."""
        ...
    async def save_told_fact(self, profile_id: UUID, told: ToldFact) -> None:
        """Keeps the fact with its kind embedding. A fact told again only gets the new `told_at`. Never raises."""
        ...
    async def load_recent_places(self) -> list[tuple[UUID, str, float]]:
        """(profile_id, place, epoch seconds) for each Look whose photo showed a place within the place window, oldest
        first. Empty when there are none or the query failed."""
        ...
    async def save_hunt(self, hunt: Hunt) -> None:
        """Keeps a new hunt with its targets. Never raises."""
        ...
    async def save_tick(self, hunt_ref: UUID, target: Target) -> None:
        """Keeps a found target. Never raises."""
        ...
    async def end_hunt(self, hunt_ref: UUID, ended_at: datetime, reason: EndReason) -> None:
        """Marks a hunt as ended, once. Never raises."""
        ...
    async def load_active_hunts(self) -> list[Hunt]:
        """The hunts that have neither ended nor timed out. Empty when there are none or the query failed."""
        ...
    async def list_hunts(self, profile_id: UUID, limit: int) -> list[StoredHunt]:
        """The profile's hunts, newest first, with the memory collected for each found target. Raises
        StoreUnavailable."""
        ...


class DisabledStore:
    async def open(self) -> None:
        pass

    async def close(self) -> None:
        pass

    async def status(self) -> str:
        return "disabled"

    async def record_look(self, record: LookRecord) -> None:
        pass

    async def collect(
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_at: datetime | None = None
    ) -> int | None:
        raise StoreUnavailable("There is no database.")

    async def list_memories(self, profile_id: UUID, limit: int) -> list[Memory]:
        raise StoreUnavailable("There is no database.")

    async def memory_image(self, profile_id: UUID, memory_id: int) -> bytes | None:
        raise StoreUnavailable("There is no database.")

    async def load_told_facts(self, profile_id: UUID) -> list[ToldFact]:
        return []

    async def load_recent_places(self) -> list[tuple[UUID, str, float]]:
        return []

    async def save_told_fact(self, profile_id: UUID, told: ToldFact) -> None:
        pass

    async def save_hunt(self, hunt: Hunt) -> None:
        pass

    async def save_tick(self, hunt_ref: UUID, target: Target) -> None:
        pass

    async def end_hunt(self, hunt_ref: UUID, ended_at: datetime, reason: EndReason) -> None:
        pass

    async def load_active_hunts(self) -> list[Hunt]:
        return []

    async def list_hunts(self, profile_id: UUID, limit: int) -> list[StoredHunt]:
        raise StoreUnavailable("There is no database.")


class PostgresStore:
    def __init__(self, database_url: str, max_size: int) -> None:
        self._pool = AsyncConnectionPool(
            database_url,
            min_size=1,
            max_size=max_size,
            open=False,
            # Hosted Postgres drops idle connections; checking on checkout replaces a dead one before it is used.
            check=AsyncConnectionPool.check_connection,
            # prepare_threshold=None keeps the pool usable behind a transaction-mode pooler URL.
            kwargs={"connect_timeout": 5, "prepare_threshold": None},
        )
        self._status_lock = asyncio.Lock()
        self._status = "unreachable"
        self._status_at = float("-inf")

    async def open(self) -> None:
        if sys.platform == "win32" and isinstance(asyncio.get_running_loop(), asyncio.ProactorEventLoop):
            raise RuntimeError("psycopg needs a selector event loop: start uvicorn with --loop asyncio:SelectorEventLoop")
        # wait=False: a database that is waking up must not hold back startup.
        await self._pool.open(wait=False)

    async def close(self) -> None:
        await self._pool.close()

    async def status(self) -> str:
        # /health is unauthenticated, so the answer is cached: a flood of checks shares one query every few seconds
        # instead of taking pool connections away from Looks.
        async with self._status_lock:
            if time.monotonic() - self._status_at > _STATUS_TTL_S:
                try:
                    async with self._pool.connection(timeout=5) as conn:
                        await conn.execute("select 1")
                    self._status = "connected"
                except Exception:
                    log.exception("database health check failed")
                    self._status = "unreachable"
                self._status_at = time.monotonic()
            return self._status

    async def record_look(self, record: LookRecord) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                await conn.execute(
                    "insert into profiles (id) values (%s) on conflict (id) do nothing", (record.profile_id,)
                )
                await conn.execute(
                    """insert into looks (id, profile_id, client_look_id, source, captured_at, width, height, status,
                           name, confidence, fact, fact_sources, rarity, duration_ms, kind, place, photo_place)
                       values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
                    (
                        record.look_ref, record.profile_id, record.client_look_id, record.source, record.captured_at,
                        record.width, record.height, record.status, record.name, record.confidence, record.fact,
                        Jsonb(record.fact_sources), record.rarity, record.duration_ms, record.kind, record.place,
                        record.photo_place,
                    ),
                )
        except Exception:
            log.exception("failed to record look %s", record.look_ref)

    async def collect(
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_at: datetime | None = None
    ) -> int | None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                # The fields are copied from the stored Look, so the client cannot supply card text. A repeat inserts
                # nothing and the existing row is read back. A headset clock ahead of the server is capped at now().
                cursor = await conn.execute(
                    """insert into memories (profile_id, look_ref, name, kind, fact, sources, rarity, place, image,
                                             created_at)
                       select profile_id, id, name, kind, fact, fact_sources, rarity, place, %s,
                              least(coalesce(%s::timestamptz, now()), now()) from looks
                       where id = %s and profile_id = %s and status = 'done' and name is not null
                       on conflict (look_ref) do nothing
                       returning id""",
                    (image, collected_at, look_ref, profile_id),
                )
                row = await cursor.fetchone()
                if row is None:
                    cursor = await conn.execute(
                        "select id from memories where look_ref = %s and profile_id = %s", (look_ref, profile_id)
                    )
                    row = await cursor.fetchone()
        except Exception as exc:
            log.exception("failed to collect look %s", look_ref)
            raise StoreUnavailable("The memory could not be saved.") from exc
        return row[0] if row else None

    async def list_memories(self, profile_id: UUID, limit: int) -> list[Memory]:
        try:
            async with self._pool.connection(timeout=10) as conn:
                cursor = await conn.execute(
                    """select id, look_ref, name, kind, fact, sources, rarity, place, created_at from memories
                       where profile_id = %s order by created_at desc, id desc limit %s""",
                    (profile_id, limit),
                )
                return [Memory(*row) for row in await cursor.fetchall()]
        except Exception as exc:
            log.exception("failed to list memories")
            raise StoreUnavailable("The memories could not be read.") from exc

    async def memory_image(self, profile_id: UUID, memory_id: int) -> bytes | None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                cursor = await conn.execute(
                    "select image from memories where id = %s and profile_id = %s", (memory_id, profile_id)
                )
                row = await cursor.fetchone()
        except Exception as exc:
            log.exception("failed to read memory %s", memory_id)
            raise StoreUnavailable("The memory could not be read.") from exc
        return bytes(row[0]) if row else None

    async def load_told_facts(self, profile_id: UUID) -> list[ToldFact]:
        try:
            async with self._pool.connection(timeout=5) as conn:
                cursor = await conn.execute(
                    """select kind, fact, kind_embedding::text, extract(epoch from told_at) from told_facts
                       where profile_id = %s order by told_at desc, id desc limit %s""",
                    (profile_id, TOLD_FACTS_KEPT),
                )
                rows = await cursor.fetchall()
        except Exception:
            log.exception("failed to load told facts")
            return []
        return [ToldFact(kind, fact, _parse_vector(vector), float(at)) for kind, fact, vector, at in reversed(rows)]

    async def load_recent_places(self) -> list[tuple[UUID, str, float]]:
        try:
            async with self._pool.connection(timeout=30) as conn:
                cursor = await conn.execute(
                    """select profile_id, photo_place, extract(epoch from created_at) from looks
                       where photo_place is not null and created_at > now() - make_interval(hours => %s)
                       order by created_at, id""",
                    (PLACE_WINDOW_HOURS,),
                )
                rows = await cursor.fetchall()
        except Exception:
            log.exception("failed to load recent places")
            return []
        return [(profile_id, place, float(at)) for profile_id, place, at in rows]

    async def save_told_fact(self, profile_id: UUID, told: ToldFact) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                await conn.execute(
                    "insert into profiles (id) values (%s) on conflict (id) do nothing", (profile_id,)
                )
                await conn.execute(
                    """insert into told_facts (profile_id, kind, kind_embedding, fact, told_at)
                       values (%s, %s, %s::vector, %s, to_timestamp(%s))
                       on conflict (profile_id, fact) do update set told_at = excluded.told_at""",
                    (profile_id, told.kind, _vector_text(told.embedding), told.fact, told.told_at),
                )
                await conn.execute(
                    """delete from told_facts where profile_id = %s and id not in
                       (select id from told_facts where profile_id = %s order by told_at desc, id desc limit %s)""",
                    (profile_id, profile_id, TOLD_FACTS_KEPT),
                )
        except Exception:
            log.exception("failed to save a told fact")


    async def save_hunt(self, hunt: Hunt) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                async with conn.transaction():
                    await conn.execute(
                        "insert into profiles (id) values (%s) on conflict (id) do nothing", (hunt.profile_id,)
                    )
                    await conn.execute(
                        """insert into hunts (id, profile_id, started_at, ends_at, ended_at, end_reason, place)
                           values (%s, %s, %s, %s, %s, %s, %s) on conflict (id) do nothing""",
                        (hunt.hunt_ref, hunt.profile_id, hunt.started_at, hunt.ends_at, hunt.ended_at,
                         hunt.end_reason, hunt.place),
                    )
                    cursor = conn.cursor()
                    await cursor.executemany(
                        """insert into hunt_targets (hunt_id, idx, text, tier, found_at, look_ref)
                           values (%s, %s, %s, %s, %s, %s) on conflict (hunt_id, idx) do nothing""",
                        [(hunt.hunt_ref, t.index, t.text, t.tier, t.found_at, t.look_ref) for t in hunt.targets],
                    )
        except Exception:
            log.exception("failed to save hunt %s", hunt.hunt_ref)

    async def save_tick(self, hunt_ref: UUID, target: Target) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                await conn.execute(
                    "update hunt_targets set found_at = %s, look_ref = %s where hunt_id = %s and idx = %s",
                    (target.found_at, target.look_ref, hunt_ref, target.index),
                )
        except Exception:
            log.exception("failed to save a tick for hunt %s", hunt_ref)

    async def end_hunt(self, hunt_ref: UUID, ended_at: datetime, reason: EndReason) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                await conn.execute(
                    "update hunts set ended_at = %s, end_reason = %s where id = %s and ended_at is null",
                    (ended_at, reason, hunt_ref),
                )
        except Exception:
            log.exception("failed to end hunt %s", hunt_ref)

    async def load_active_hunts(self) -> list[Hunt]:
        try:
            async with self._pool.connection(timeout=30) as conn:
                found = await _read_hunts(conn, "where ended_at is null and ends_at > now() order by started_at", ())
        except Exception:
            log.exception("failed to load active hunts")
            return []
        return [stored.hunt for stored in found]

    async def list_hunts(self, profile_id: UUID, limit: int) -> list[StoredHunt]:
        try:
            async with self._pool.connection(timeout=10) as conn:
                return await _read_hunts(
                    conn, "where profile_id = %s order by started_at desc, id limit %s", (profile_id, limit))
        except Exception as exc:
            log.exception("failed to list hunts")
            raise StoreUnavailable("The hunts could not be read.") from exc


async def _read_hunts(conn, tail: str, params: tuple) -> list[StoredHunt]:
    """The hunts a `where ... order by ...` tail selects, in that order, each with its targets and, per found target,
    the memory collected from the finding Look."""
    cursor = await conn.execute(
        f"select id, profile_id, started_at, ends_at, ended_at, end_reason, place from hunts {tail}", params
    )
    rows = await cursor.fetchall()
    cursor = await conn.execute(
        """select t.hunt_id, t.idx, t.text, t.tier, t.found_at, t.look_ref, m.id from hunt_targets t
           left join memories m on m.look_ref = t.look_ref where t.hunt_id = any(%s::uuid[]) order by t.hunt_id, t.idx""",
        ([row[0] for row in rows],),
    )
    targets: dict[UUID, list[tuple[Target, int | None]]] = {}
    for hunt_id, idx, text, tier, found_at, look_ref, memory_id in await cursor.fetchall():
        targets.setdefault(hunt_id, []).append((Target(idx, text, tier, found_at, look_ref), memory_id))
    return [
        StoredHunt(
            Hunt(hunt_ref=hunt_id, profile_id=profile_id, started_at=started_at, ends_at=ends_at,
                 targets=tuple(t for t, _ in targets.get(hunt_id, [])), place=place, ended_at=ended_at,
                 end_reason=end_reason),
            tuple(m for _, m in targets.get(hunt_id, [])),
        )
        for hunt_id, profile_id, started_at, ends_at, ended_at, end_reason, place in rows
    ]


def _vector_text(embedding: array | None) -> str | None:
    return None if embedding is None else "[" + ",".join(map(repr, embedding)) + "]"


def _parse_vector(text: str | None) -> array | None:
    return None if text is None else array("f", json.loads(text))


def make_store(database_url: str | None, max_size: int) -> Store:
    return PostgresStore(database_url, max_size) if database_url else DisabledStore()
