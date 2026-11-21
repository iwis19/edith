"""Runs against a real Postgres with pgvector when TEST_DATABASE_URL is set. Every test works in its own schema, which is
dropped afterwards, so a shared database is left as it was."""
import asyncio
import os
import uuid
from array import array
from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, datetime, timedelta

import psycopg
import pytest

from edith_backend.config import Settings
from edith_backend.db import TOLD_FACTS_KEPT, LookRecord, PostgresStore, StoreUnavailable, ToldFact
from edith_backend.hunts import Hunt, Hunts, Target
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
    assert migrate(schema_url) == ["001_init", "002_collect", "003_told_facts", "004_photo_place", "005_hunts"]
    assert migrate(schema_url) == []


def test_concurrent_migrations_take_turns(schema_url):
    with ThreadPoolExecutor(4) as pool:
        results = list(pool.map(lambda _: migrate(schema_url), range(4)))
    assert sorted(results) == [[], [], [], ["001_init", "002_collect", "003_told_facts", "004_photo_place", "005_hunts"]]


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


def a_look(profile: uuid.UUID, **overrides) -> LookRecord:
    fields = dict(
        look_ref=uuid.uuid4(), profile_id=profile, client_look_id=1, source="pinch", captured_at=datetime.now(UTC),
        width=640, height=480, status="done", name="mug", confidence=0.9, fact="Handles came late.",
        fact_sources=[{"title": "t", "url": "https://example.com"}], rarity="rare", duration_ms=812,
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

        first_id = await store.collect(profile, first.look_ref, b"crop-1",
                                       datetime(2026, 11, 21, 8, tzinfo=UTC))
        assert await store.collect(profile, first.look_ref, b"other bytes") == first_id
        second_id = await store.collect(profile, second.look_ref, b"crop-2")
        assert await store.collect(profile, failed.look_ref, b"x") is None
        assert await store.collect(other, first.look_ref, b"x") is None
        assert await store.collect(profile, uuid.uuid4(), b"x") is None

        memories = await store.list_memories(profile, 10)
        assert [(m.id, m.name, m.rarity) for m in memories] == [(second_id, "lamp", "epic"), (first_id, "mug", "rare")]
        assert memories[1].sources == first.fact_sources
        assert memories[1].created_at == datetime(2026, 11, 21, 8, tzinfo=UTC)
        assert [(m.kind, m.place) for m in memories] == [(None, None), ("mug", "Waterloo, Ontario, Canada")]
        assert await store.list_memories(profile, 1) == memories[:1]
        assert await store.list_memories(other, 10) == []
        assert await store.memory_image(profile, first_id) == b"crop-1"
        assert await store.memory_image(other, first_id) is None
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


async def a_hunt(store: PostgresStore, profile: uuid.UUID, started: datetime, **overrides) -> Hunt:
    hunt = Hunt(
        hunt_ref=uuid.uuid4(), profile_id=profile, started_at=started, ends_at=started + timedelta(seconds=300),
        place="Waterloo, Ontario, Canada",
        targets=tuple([Target(0, "something red"), Target(1, "something round"), Target(2, "something wooden"),
                       Target(3, "something Uncommon or better", "uncommon"),
                       Target(4, "something Rare or better", "rare")]),
    )
    hunt = Hunt(**{**hunt.__dict__, **overrides})
    await store.save_hunt(hunt)
    return hunt


async def test_a_hunt_round_trips_with_its_ticks_its_end_and_the_collected_memory(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile, other = uuid.uuid4(), uuid.uuid4()
        now = datetime.now(UTC)
        older = await a_hunt(store, profile, now - timedelta(hours=1))
        hunt = await a_hunt(store, profile, now)
        await a_hunt(store, other, now)
        found_look, uncollected_look = a_look(profile), a_look(profile)
        await store.record_look(found_look)
        await store.save_tick(hunt.hunt_ref, Target(0, "something red", None, now, found_look.look_ref))
        await store.save_tick(hunt.hunt_ref, Target(3, "something Uncommon or better", "uncommon", now,
                                                    uncollected_look.look_ref))
        memory_id = await store.collect(profile, found_look.look_ref, b"crop")
        await store.end_hunt(hunt.hunt_ref, now + timedelta(seconds=90), "stopped")
        await store.end_hunt(hunt.hunt_ref, now + timedelta(seconds=99), "found_all")
        await store.save_hunt(hunt)

        listed = await store.list_hunts(profile, 10)
        assert [s.hunt.hunt_ref for s in listed] == [hunt.hunt_ref, older.hunt_ref]
        newest = listed[0]
        assert (newest.hunt.end_reason, newest.hunt.ended_at, newest.hunt.place) == (
            "stopped", now + timedelta(seconds=90), "Waterloo, Ontario, Canada")
        assert [(t.index, t.text, t.tier) for t in newest.hunt.targets][3] == (3, "something Uncommon or better", "uncommon")
        assert [t.look_ref for t in newest.hunt.targets] == [found_look.look_ref, None, None,
                                                            uncollected_look.look_ref, None]
        assert [t.found_at is not None for t in newest.hunt.targets] == [True, False, False, True, False]
        assert newest.memory_ids == (memory_id, None, None, None, None)
        assert len(await store.list_hunts(profile, 1)) == 1
        assert [s.hunt.profile_id for s in await store.list_hunts(other, 10)] == [other]
    finally:
        await store.close()


async def test_only_hunts_that_have_neither_ended_nor_timed_out_are_loaded_as_active(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile = uuid.uuid4()
        now = datetime.now(UTC)
        running = await a_hunt(store, profile, now - timedelta(seconds=60))
        stopped = await a_hunt(store, profile, now - timedelta(seconds=30))
        await a_hunt(store, profile, now - timedelta(seconds=900))
        await store.end_hunt(stopped.hunt_ref, now, "stopped")
        [loaded] = await store.load_active_hunts()
        assert loaded.hunt_ref == running.hunt_ref
        assert (loaded.targets, loaded.ends_at) == (running.targets, running.ends_at)
    finally:
        await store.close()


async def test_hunts_go_through_the_service_to_postgres_and_back_after_a_restart(schema_url):
    migrate(schema_url)
    store = PostgresStore(schema_url, max_size=2)
    await store.open()
    try:
        profile = uuid.uuid4()

        async def draft(place, interests):
            return [Target(0, "something red"), Target(1, "something round"), Target(2, "something wooden"),
                    Target(3, "something Uncommon or better", "uncommon"), Target(4, "something Rare or better", "rare")]

        hunts = Hunts(store, draft)
        started = await hunts.start(profile)
        look_ref = uuid.uuid4()
        assert hunts.tick(profile, look_ref, 1, None) is not None
        await hunts.settle()
        reborn = Hunts(store, draft)
        await reborn.restore()
        [stored] = await reborn.history(profile, 10)
        assert stored.hunt.hunt_ref == started.hunt_ref
        assert (reborn.current(profile).found, reborn.current(profile).targets[1].look_ref) == (1, look_ref)
        reborn.stop(profile)
        await reborn.settle()
        assert (await store.list_hunts(profile, 1))[0].hunt.end_reason == "stopped"
    finally:
        await store.close()


async def test_a_database_that_fails_loses_no_hunt_and_the_list_reports_it(schema_url):
    store = PostgresStore(schema_url, max_size=1)
    await store.open()
    try:
        profile = uuid.uuid4()
        hunt = Hunt(hunt_ref=uuid.uuid4(), profile_id=profile, started_at=datetime.now(UTC),
                    ends_at=datetime.now(UTC), targets=())
        await store.save_hunt(hunt)
        await store.save_tick(hunt.hunt_ref, Target(0, "x"))
        await store.end_hunt(hunt.hunt_ref, datetime.now(UTC), "stopped")
        assert await store.load_active_hunts() == []
        with pytest.raises(StoreUnavailable):
            await store.list_hunts(profile, 5)
    finally:
        await store.close()
