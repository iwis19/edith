"""Applies each migrations/*.sql not yet recorded in schema_migrations, in name order, one transaction per file.

Usage: uv run python -m edith_backend.migrate
Reads DATABASE_URL from the environment or backend/.env.
"""
from pathlib import Path

import psycopg

from .config import Settings

MIGRATIONS = Path(__file__).resolve().parent / "migrations"
# Any fixed key works; it only has to be the same for every run.
_LOCK_KEY = 0x6564697468


def migrate(url: str) -> list[str]:
    applied: list[str] = []
    with psycopg.connect(url, autocommit=True) as conn:
        # Two runs at once (two deploys starting together) would both see a file as new; the lock makes them take
        # turns, and it is released when the connection closes.
        conn.execute("select pg_advisory_lock(%s)", (_LOCK_KEY,))
        conn.execute(
            "create table if not exists schema_migrations"
            " (version text primary key, applied_at timestamptz not null default now())"
        )
        done = {row[0] for row in conn.execute("select version from schema_migrations")}
        for path in sorted(MIGRATIONS.glob("*.sql")):
            if path.stem in done:
                continue
            with conn.transaction():
                conn.execute(path.read_text(encoding="utf-8"))
                conn.execute("insert into schema_migrations (version) values (%s)", (path.stem,))
            applied.append(path.stem)
    return applied


if __name__ == "__main__":
    url = Settings().database_url
    if not url:
        raise SystemExit("DATABASE_URL is not set in the environment or backend/.env.")
    for version in migrate(url):
        print("applied", version)
    print("schema is up to date")
