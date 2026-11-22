"""Applies each migrations/*.sql not yet recorded in schema_migrations, in name order, one transaction per file.

The migrations_trails/ folder is a second, independent sequence for the trails tables. It runs against TRAILS_DATABASE_URL
when that is set and against DATABASE_URL otherwise, and records itself in trail_schema_migrations.

Usage: uv run python -m edith_backend.migrate
Reads DATABASE_URL and TRAILS_DATABASE_URL from the environment or backend/.env.
"""
from pathlib import Path

import psycopg

from .config import Settings

MIGRATIONS = Path(__file__).resolve().parent / "migrations"
TRAIL_MIGRATIONS = Path(__file__).resolve().parent / "migrations_trails"
# Any fixed key works; it only has to be the same for every run.
_LOCK_KEY = 0x6564697468


def migrate(url: str, folder: Path = MIGRATIONS, table: str = "schema_migrations") -> list[str]:
    applied: list[str] = []
    with psycopg.connect(url, autocommit=True) as conn:
        # Two runs at once (two deploys starting together) would both see a file as new; the lock makes them take
        # turns, and it is released when the connection closes.
        conn.execute("select pg_advisory_lock(%s)", (_LOCK_KEY,))
        conn.execute(
            f"create table if not exists {table}"
            " (version text primary key, applied_at timestamptz not null default now())"
        )
        done = {row[0] for row in conn.execute(f"select version from {table}")}
        for path in sorted(folder.glob("*.sql")):
            if path.stem in done:
                continue
            with conn.transaction():
                conn.execute(path.read_text(encoding="utf-8"))
                conn.execute(f"insert into {table} (version) values (%s)", (path.stem,))
            applied.append(path.stem)
    return applied


if __name__ == "__main__":
    settings = Settings()
    url = settings.database_url
    if not url:
        raise SystemExit("DATABASE_URL is not set in the environment or backend/.env.")
    for version in migrate(url):
        print("applied", version)
    for version in migrate(settings.trails_database_url or url, TRAIL_MIGRATIONS, "trail_schema_migrations"):
        print("applied trails", version)
    print("schema is up to date")
