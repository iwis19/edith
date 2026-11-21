"""Runs against a real Postgres with pgvector when TEST_DATABASE_URL is set. Every test works in its own schema, which is
dropped afterwards, so a shared database is left as it was."""
import asyncio
import os
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, datetime

import psycopg
import pytest

from edith_backend.db import LookRecord, PostgresStore
from edith_backend.migrate import migrate

URL = os.environ.get("TEST_DATABASE_URL")
pytestmark = pytest.mark.skipif(not URL, reason="TEST_DATABASE_URL is not set")


@pytest.fixture
def schema_url():
    schema = f"edith_test_{uuid.uuid4().hex[:12]}"
    with psycopg.connect(URL, autocommit=True) as conn:
        conn.execute(f"create schema {schema}")
    try:
        yield psycopg.conninfo.make_conninfo(URL, options=f"-c search_path={schema},public")
    finally:
        with psycopg.connect(URL, autocommit=True) as conn:
            conn.execute(f"drop schema {schema} cascade")


def test_migrate_applies_each_file_once(schema_url):
    assert migrate(schema_url) == ["001_init"]
    assert migrate(schema_url) == []


def test_concurrent_migrations_take_turns(schema_url):
    with ThreadPoolExecutor(4) as pool:
        results = list(pool.map(lambda _: migrate(schema_url), range(4)))
    assert sorted(results) == [[], [], [], ["001_init"]]


async def test_health_checks_share_one_query_while_cached(schema_url):
    store = PostgresStore(schema_url, max_size=1)
    await store.open()
    try:
        statuses = await asyncio.gather(*(store.status() for _ in range(50)))
        assert set(statuses) == {"connected"}
        assert store._pool.get_stats().get("requests_num", 0) == 1
    finally:
        await store.close()


async def test_the_store_records_a_look_for_a_new_profile(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        assert await store.status() == "connected"
        profile = uuid.uuid4()
        record = LookRecord(
            look_ref=uuid.uuid4(), profile_id=profile, client_look_id=1, source="pinch",
            captured_at=datetime.now(UTC), width=640, height=480, status="done", name="mug", confidence=0.9,
            fact="Handles came late.", fact_sources=[{"title": "t", "url": "https://example.com"}], rarity="mythic",
            duration_ms=812,
        )
        await store.record_look(record)
        await store.record_look(LookRecord(**{**record.__dict__, "look_ref": uuid.uuid4()}))
    finally:
        await store.close()
    with psycopg.connect(schema_url) as conn:
        rows = conn.execute("select name, rarity, fact_sources from looks where profile_id = %s", (profile,)).fetchall()
    assert rows == [("mug", "mythic", record.fact_sources)] * 2
