import asyncio
import json
import logging
import sys
import time
from array import array
from dataclasses import dataclass
from datetime import UTC, date, datetime
from typing import Protocol
from uuid import UUID

from psycopg_pool import AsyncConnectionPool

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
    rarity: str | None
    duration_ms: int
    kind: str | None = None
    place: str | None = None
    photo_place: str | None = None


@dataclass(frozen=True)
class Memory:
    id: int
    name: str
    fact: str | None
    rarity: str | None
    place: str | None
    collected_on: date
    point: tuple[float, float] | None = None


def collection_date(collected_at: datetime | None, now: datetime) -> date:
    """The calendar date of a collection in the headset's own offset. A time ahead of `now` counts as `now`, and with
    no time the date is the UTC date of `now`."""
    if collected_at is None:
        return now.astimezone(UTC).date()
    return min(collected_at, now).astimezone(collected_at.tzinfo).date()


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
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_on: date
    ) -> int | None:
        """Saves the finished Look as a memory, once per Look, and returns the memory's id. None when the profile has
        no finished Look with that ref. Raises StoreUnavailable."""
        ...
    async def list_memories(self, profile_id: UUID, limit: int) -> list[Memory]: ...
    async def place_to_locate(self, profile_id: UUID, memory_id: int) -> str | None:
        """The memory's place when it has one and no point yet, else None."""
        ...
    async def set_place_point(self, memory_id: int, point: tuple[float, float]) -> None:
        """Saves the point of the memory's place unless one is already saved."""
        ...
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
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_on: date
    ) -> int | None:
        raise StoreUnavailable("There is no database.")

    async def list_memories(self, profile_id: UUID, limit: int) -> list[Memory]:
        raise StoreUnavailable("There is no database.")

    async def place_to_locate(self, profile_id: UUID, memory_id: int) -> str | None:
        raise StoreUnavailable("There is no database.")

    async def set_place_point(self, memory_id: int, point: tuple[float, float]) -> None:
        raise StoreUnavailable("There is no database.")

    async def memory_image(self, profile_id: UUID, memory_id: int) -> bytes | None:
        raise StoreUnavailable("There is no database.")

    async def load_told_facts(self, profile_id: UUID) -> list[ToldFact]:
        return []

    async def load_recent_places(self) -> list[tuple[UUID, str, float]]:
        return []

    async def save_told_fact(self, profile_id: UUID, told: ToldFact) -> None:
        pass


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
                           name, confidence, fact, rarity, duration_ms, kind, place, photo_place)
                       values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
                    (
                        record.look_ref, record.profile_id, record.client_look_id, record.source, record.captured_at,
                        record.width, record.height, record.status, record.name, record.confidence, record.fact,
                        record.rarity, record.duration_ms, record.kind, record.place,
                        record.photo_place,
                    ),
                )
        except Exception:
            log.exception("failed to record look %s", record.look_ref)

    async def collect(
        self, profile_id: UUID, look_ref: UUID, image: bytes, collected_on: date
    ) -> int | None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                # The fields are copied from the stored Look, so the client cannot supply card text. A repeat inserts
                # nothing and the existing row is read back.
                cursor = await conn.execute(
                    """insert into memories (profile_id, look_ref, name, fact, rarity, place, image, collected_on)
                       select profile_id, id, name, fact, rarity, place, %s, %s from looks
                       where id = %s and profile_id = %s and status = 'done' and name is not null
                       on conflict (look_ref) do nothing
                       returning id""",
                    (image, collected_on, look_ref, profile_id),
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
                    """select id, name, fact, rarity, place, collected_on, place_lat, place_lon from memories
                       where profile_id = %s order by collected_on desc, id desc limit %s""",
                    (profile_id, limit),
                )
                return [
                    Memory(*row[:6], point=None if row[6] is None or row[7] is None else (row[6], row[7]))
                    for row in await cursor.fetchall()
                ]
        except Exception as exc:
            log.exception("failed to list memories")
            raise StoreUnavailable("The memories could not be read.") from exc

    async def place_to_locate(self, profile_id: UUID, memory_id: int) -> str | None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                cursor = await conn.execute(
                    """select place from memories
                       where id = %s and profile_id = %s and place is not null and place_lat is null""",
                    (memory_id, profile_id),
                )
                row = await cursor.fetchone()
        except Exception as exc:
            log.exception("failed to read the place of memory %s", memory_id)
            raise StoreUnavailable("The memory could not be read.") from exc
        return row[0] if row else None

    async def set_place_point(self, memory_id: int, point: tuple[float, float]) -> None:
        try:
            async with self._pool.connection(timeout=10) as conn:
                await conn.execute(
                    "update memories set place_lat = %s, place_lon = %s where id = %s and place_lat is null",
                    (*point, memory_id),
                )
        except Exception as exc:
            log.exception("failed to save the place point of memory %s", memory_id)
            raise StoreUnavailable("The memory could not be saved.") from exc

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


def _vector_text(embedding: array | None) -> str | None:
    return None if embedding is None else "[" + ",".join(map(repr, embedding)) + "]"


def _parse_vector(text: str | None) -> array | None:
    return None if text is None else array("f", json.loads(text))


def make_store(database_url: str | None, max_size: int) -> Store:
    return PostgresStore(database_url, max_size) if database_url else DisabledStore()
