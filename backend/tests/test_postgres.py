"""Runs against a real Postgres with pgvector when TEST_DATABASE_URL is set. Every test works in its own schema, which is
dropped afterwards, so a shared database is left as it was."""
import asyncio
import os
import uuid
from array import array
from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, date, datetime

import psycopg
import pytest

from edith_backend.config import Settings
from edith_backend.db import TOLD_FACTS_KEPT, LookRecord, PostgresStore, ToldFact
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
    assert migrate(schema_url) == ["001_init", "002_collect", "003_told_facts", "004_photo_place", "005_drop_sources", "006_memory_date", "007_place_point"]
    assert migrate(schema_url) == []


def test_concurrent_migrations_take_turns(schema_url):
    with ThreadPoolExecutor(4) as pool:
        results = list(pool.map(lambda _: migrate(schema_url), range(4)))
    assert sorted(results) == [[], [], [], ["001_init", "002_collect", "003_told_facts", "004_photo_place", "005_drop_sources", "006_memory_date", "007_place_point"]]


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
            fact="Handles came late.", rarity="mythic", duration_ms=812,
        )
        await store.record_look(record)
        await store.record_look(LookRecord(**{**record.__dict__, "look_ref": uuid.uuid4()}))
    finally:
        await store.close()
    with psycopg.connect(schema_url) as conn:
        rows = conn.execute("select name, rarity, fact from looks where profile_id = %s", (profile,)).fetchall()
    assert rows == [("mug", "mythic", "Handles came late.")] * 2


def a_look(profile: uuid.UUID, **overrides) -> LookRecord:
    fields = dict(
        look_ref=uuid.uuid4(), profile_id=profile, client_look_id=1, source="pinch", captured_at=datetime.now(UTC),
        width=640, height=480, status="done", name="mug", confidence=0.9, fact="Handles came late.",
        rarity="rare", duration_ms=812,
    )
    return LookRecord(**{**fields, **overrides})


async def test_collecting_copies_the_stored_look_once_and_lists_it_newest_first(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile, other = uuid.uuid4(), uuid.uuid4()
        first = a_look(profile, kind="mug", place="Waterloo, Ontario, Canada")
        second, failed = a_look(profile, name="lamp", rarity="epic"), a_look(
            profile, status="error", name=None, fact=None, rarity=None)
        for record in (first, second, failed, a_look(other)):
            await store.record_look(record)

        first_id = await store.collect(profile, first.look_ref, b"crop-1", date(2026, 11, 20))
        assert await store.collect(profile, first.look_ref, b"other bytes", date(2026, 11, 22)) == first_id
        second_id = await store.collect(profile, second.look_ref, b"crop-2", date(2026, 11, 21))
        assert await store.collect(profile, failed.look_ref, b"x", date(2026, 11, 21)) is None
        assert await store.collect(other, first.look_ref, b"x", date(2026, 11, 21)) is None
        assert await store.collect(profile, uuid.uuid4(), b"x", date(2026, 11, 21)) is None

        memories = await store.list_memories(profile, 10)
        assert [(m.id, m.name, m.rarity) for m in memories] == [(second_id, "lamp", "epic"), (first_id, "mug", "rare")]
        assert memories[1].fact == first.fact
        assert [m.collected_on for m in memories] == [date(2026, 11, 21), date(2026, 11, 20)]
        assert [m.place for m in memories] == [None, "Waterloo, Ontario, Canada"]
        assert await store.list_memories(profile, 1) == memories[:1]
        assert await store.list_memories(other, 10) == []
        assert await store.memory_image(profile, first_id) == b"crop-1"
        assert await store.memory_image(other, first_id) is None
    finally:
        await store.close()


async def test_a_place_point_is_saved_once_for_a_memory_with_a_place(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile, other = uuid.uuid4(), uuid.uuid4()
        placed, unplaced = a_look(profile, place="Waterloo, Ontario, Canada"), a_look(profile)
        for record in (placed, unplaced):
            await store.record_look(record)
        placed_id = await store.collect(profile, placed.look_ref, b"a", date(2026, 11, 21))
        unplaced_id = await store.collect(profile, unplaced.look_ref, b"b", date(2026, 11, 20))

        assert await store.place_to_locate(profile, placed_id) == "Waterloo, Ontario, Canada"
        assert await store.place_to_locate(other, placed_id) is None
        assert await store.place_to_locate(profile, unplaced_id) is None
        await store.set_place_point(placed_id, (43.47235, -80.54491))
        await store.set_place_point(placed_id, (1.0, 2.0))
        assert await store.place_to_locate(profile, placed_id) is None
        points = [m.point for m in await store.list_memories(profile, 10)]
        assert points == [(43.47235, -80.54491), None]
    finally:
        await store.close()


def test_the_embedding_column_matches_the_configured_dimensions(schema_url):
    migrate(schema_url)
    with psycopg.connect(schema_url) as conn:
        [(dimensions,)] = conn.execute(
            "select atttypmod from pg_attribute where attrelid = 'memories'::regclass and attname = 'embedding'"
        ).fetchall()
    assert dimensions == Settings(_env_file=None).embedding_dimensions


def test_the_told_fact_embedding_column_matches_the_configured_dimensions(schema_url):
    migrate(schema_url)
    with psycopg.connect(schema_url) as conn:
        [(dimensions,)] = conn.execute(
            "select atttypmod from pg_attribute where attrelid = 'told_facts'::regclass and attname = 'kind_embedding'"
        ).fetchall()
    assert dimensions == Settings(_env_file=None).embedding_dimensions


async def test_told_facts_keep_their_kind_embedding_per_profile_and_load_oldest_first(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile, other = uuid.uuid4(), uuid.uuid4()
        size = Settings(_env_file=None).embedding_dimensions
        vector = array("f", [0.5] + [0.0] * (size - 1))
        await store.save_told_fact(profile, ToldFact("mug", "Mugs came first.", vector, 1000.0))
        await store.save_told_fact(profile, ToldFact("lamp", "Lamps came second.", None, 2000.0))
        await store.save_told_fact(profile, ToldFact("mug", "Mugs came first.", vector, 3000.0))
        await store.save_told_fact(other, ToldFact("mug", "Mugs came first.", vector, 1.0))

        loaded = await store.load_told_facts(profile)
        assert [(t.kind, t.fact, t.told_at) for t in loaded] == [
            ("lamp", "Lamps came second.", 2000.0), ("mug", "Mugs came first.", 3000.0)]
        assert loaded[0].embedding is None
        assert list(loaded[1].embedding) == list(vector)
        assert len(await store.load_told_facts(other)) == 1
        assert await store.load_told_facts(uuid.uuid4()) == []
    finally:
        await store.close()


async def test_only_the_newest_told_facts_are_kept(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile = uuid.uuid4()
        for n in range(TOLD_FACTS_KEPT + 5):
            await store.save_told_fact(profile, ToldFact("mug", f"Fact {n}.", None, float(n)))
        loaded = await store.load_told_facts(profile)
        assert [t.fact for t in loaded] == [f"Fact {n}." for n in range(5, TOLD_FACTS_KEPT + 5)]
    finally:
        await store.close()


async def test_recent_places_load_only_photo_places_within_the_window_oldest_first(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile, other = uuid.uuid4(), uuid.uuid4()
        for record in (
            a_look(profile, photo_place="Lisbon, Portugal"),
            a_look(profile, place="Waterloo, Ontario, Canada"),
            a_look(profile, photo_place="Porto, Portugal"),
            a_look(other, photo_place="Paris, France"),
        ):
            await store.record_look(record)
        with psycopg.connect(schema_url, autocommit=True) as conn:
            conn.execute("update looks set created_at = now() - interval '7 hours' where photo_place = 'Lisbon, Portugal'")
        places = await store.load_recent_places()
    finally:
        await store.close()
    assert [(p, place) for p, place, _ in places] == [(profile, "Porto, Portugal"), (other, "Paris, France")]
