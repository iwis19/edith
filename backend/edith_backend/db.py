import asyncio
import logging
import sys
import time
from dataclasses import dataclass
from datetime import datetime
from typing import Protocol
from uuid import UUID

from psycopg.types.json import Jsonb
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
    fact_sources: list[dict]
    rarity: str | None
    duration_ms: int


class Store(Protocol):
    async def open(self) -> None: ...
    async def close(self) -> None: ...
    async def status(self) -> str: ...
    async def record_look(self, record: LookRecord) -> None: ...


class DisabledStore:
    async def open(self) -> None:
        pass

    async def close(self) -> None:
        pass

    async def status(self) -> str:
        return "disabled"

    async def record_look(self, record: LookRecord) -> None:
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
                           name, confidence, fact, fact_sources, rarity, duration_ms)
                       values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
                    (
                        record.look_ref, record.profile_id, record.client_look_id, record.source, record.captured_at,
                        record.width, record.height, record.status, record.name, record.confidence, record.fact,
                        Jsonb(record.fact_sources), record.rarity, record.duration_ms,
                    ),
                )
        except Exception:
            log.exception("failed to record look %s", record.look_ref)


def make_store(database_url: str | None, max_size: int) -> Store:
    return PostgresStore(database_url, max_size) if database_url else DisabledStore()
