# EDITH backend

A FastAPI service that holds every AI call, the search and the memory, so API keys never reach the headset. This version streams Looks from a stub Card agent: canned cards on the real event schedule, so the headset pipe works before the Gemini Card agent is wired in.

## API

Every endpoint except `/health` needs two headers:

| Header | Value |
| --- | --- |
| `X-Edith-Token` | The device token, when `DEVICE_TOKEN` is set on the backend |
| `X-Edith-Profile` | The profile's UUID, made by the headset when a profile is created |

### `GET /health`

Returns `{"status": "ok", "version": "...", "db": "connected" | "unreachable" | "disabled"}`.

### `POST /look`

A multipart form with two fields:

- `image`: the cropped JPEG, at most 2 MB and 1024 px on its longest side.
- `meta`: JSON `{"look_id": int, "source": "pinch" | "scan", "captured_at": ISO 8601, "width": int, "height": int}`.

The response is newline-delimited JSON (`application/x-ndjson`), one event per line, each with `t_ms` since the request arrived:

| Event | Fields |
| --- | --- |
| `accepted` | `look_id`, `look_ref` (the backend's ID for this Look) |
| `name` | `name`, `confidence` |
| `fact` | `text`, `sources` (`title`, `url`) |
| `rarity` | `tier`: `common`, `uncommon`, `rare`, `epic`, `legendary` or `mythic` |
| `done` | none |
| `error` | `code` (`timeout` or `agent_failed`), `message` |

The stream always ends with `done` or `error`. A bad request is refused before streaming with an HTTP error and `{"error": {"code", "message"}}`. Each finished Look is written to the `looks` table.

## Set up

Install [uv](https://docs.astral.sh/uv/), then run from this folder:

```
uv sync
```

## Run

```
uv run uvicorn edith_backend.app:app --host 127.0.0.1 --port 8000 --loop asyncio:SelectorEventLoop
```

On Windows, `--loop asyncio:SelectorEventLoop` is required when `DATABASE_URL` is set. psycopg's async mode cannot run on the default proactor loop, and the app refuses to start on it. Linux hosts need no flag.

## Configure

Copy `.env.example` to `.env`. With no `DATABASE_URL` the app runs without a database. Model IDs, the embedding size and the stub timings are settings in `edith_backend/config.py`.

## Database

The database is a Tiger Cloud service (managed Postgres) with pgvector.

1. Create a service in Tiger Cloud and copy its connection string into `.env` as `DATABASE_URL`.
2. Apply the schema:

   ```
   uv run python -m edith_backend.migrate
   ```

   Each file in `edith_backend/migrations/` runs once, in name order, and is recorded in `schema_migrations`. Rerunning applies only new files.

## Test

```
uv run pytest
```

The database tests run only when `TEST_DATABASE_URL` is set. Each test works in its own schema and drops it afterwards. To run them against a local Postgres:

```
docker run -d --rm --name edith-pg -e POSTGRES_PASSWORD=edith -p 55432:5432 pgvector/pgvector:pg17
```

Then set `TEST_DATABASE_URL=postgresql://postgres:edith@127.0.0.1:55432/postgres` and run the tests.

## Deploy to Railway

1. Create a Railway service from this repository with the root directory set to `backend`. Railway builds the `Dockerfile`.
2. Set `GEMINI_API_KEY`, `DATABASE_URL` and `DEVICE_TOKEN` on the service. The image sets `PUBLIC_SERVER=1`, so the app refuses to start without `DEVICE_TOKEN`.
3. Keep one replica: the phone's location is held in process memory.

## Layout

- `edith_backend/app.py`: routes, header and upload checks, the NDJSON stream.
- `edith_backend/agents/card.py`: the `CardAgent` protocol and the stub.
- `edith_backend/events.py`: the stream's event types and the Look metadata.
- `edith_backend/db.py`: the pooled Postgres store, or a no-op store with no database.
- `edith_backend/migrate.py` and `edith_backend/migrations/`: the schema.
