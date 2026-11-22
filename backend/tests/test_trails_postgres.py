"""Trails against a real Postgres. TEST_DATABASE_URL is plain Postgres; TEST_TIMESCALE_URL, when set, is a database with
the timescaledb extension available."""
import os
import uuid
from datetime import UTC, datetime, timedelta

import psycopg
import pytest

from edith_backend.location import LocationFix
from edith_backend.migrate import TRAIL_MIGRATIONS, migrate
from edith_backend.trails import PostgresTrailStore, cell_of, unexplored_areas

PLAIN, TIMESCALE = os.environ.get("TEST_DATABASE_URL"), os.environ.get("TEST_TIMESCALE_URL")
URLS = [pytest.param(PLAIN, id="plain", marks=pytest.mark.skipif(not PLAIN, reason="TEST_DATABASE_URL is not set")),
        pytest.param(TIMESCALE, id="timescale",
                     marks=pytest.mark.skipif(not TIMESCALE, reason="TEST_TIMESCALE_URL is not set"))]
HERE = (43.0096, -81.2737)
PROFILE, OTHER = uuid.uuid4(), uuid.uuid4()


@pytest.fixture(params=URLS)
def trail_url(request):
    schema = f"edith_trail_{uuid.uuid4().hex[:12]}"
    with psycopg.connect(request.param, autocommit=True) as conn:
        conn.execute(f"create schema {schema}")
    try:
        yield psycopg.conninfo.make_conninfo(request.param, options=f"-c search_path={schema},public")
    finally:
        with psycopg.connect(request.param, autocommit=True) as conn:
            conn.execute(f"drop schema {schema} cascade")


def fix(lat, lon, minutes=0):
    return LocationFix(lat=lat, lon=lon, accuracy_m=6.0,
                       taken_at=datetime(2026, 11, 21, 10, tzinfo=UTC) + timedelta(minutes=minutes))


def migrated(url):
    return migrate(url, TRAIL_MIGRATIONS, "trail_schema_migrations")


def test_trail_migrations_apply_once_and_only_the_timescale_database_gets_a_hypertable(trail_url):
    assert migrated(trail_url) == ["001_trail_points", "002_trail_cells"]
    assert migrated(trail_url) == []
    with psycopg.connect(trail_url) as conn:
        timescale = conn.execute("select count(*) from pg_extension where extname = 'timescaledb'").fetchone()[0]
        hyper = conn.execute("select count(*) from timescaledb_information.hypertables"
                             " where hypertable_name = 'trail_points'").fetchone()[0] if timescale else 0
        view = conn.execute("select to_regclass('trail_cells_daily') is not null").fetchone()[0]
    assert bool(timescale) == bool(hyper) == view


async def test_recorded_points_become_walked_cells_for_that_profile_only(trail_url):
    migrated(trail_url)
    trails = PostgresTrailStore(trail_url)
    await trails.open()
    try:
        adventure = uuid.uuid4()
        await trails.record(PROFILE, adventure, fix(43.00961234, -81.27371234))
        await trails.record(PROFILE, adventure, fix(43.00961234, -81.27371234))
        await trails.record(PROFILE, adventure, fix(43.0156, -81.2737, minutes=3))
        await trails.record(OTHER, uuid.uuid4(), fix(43.0236, -81.2737))
        walked = await trails.walked_cells(PROFILE, *HERE, 1500)
        assert walked == {cell_of(43.0096, -81.2737), cell_of(43.0156, -81.2737)}
        with psycopg.connect(trail_url) as conn:
            assert conn.execute("select count(*), min(lat) from trail_points where profile_id = %s",
                                (PROFILE,)).fetchone() == (2, 43.0096)
        areas = unexplored_areas(*HERE, 1500, walked)
        assert areas and not {cell_of(a.lat, a.lon) for a in areas} & walked
    finally:
        await trails.close()


async def test_a_database_without_the_tables_reads_as_unavailable_not_as_an_error(trail_url):
    trails = PostgresTrailStore(trail_url)
    await trails.open()
    try:
        assert await trails.walked_cells(PROFILE, *HERE, 1500) is None
        await trails.record(PROFILE, uuid.uuid4(), fix(*HERE))
    finally:
        await trails.close()
