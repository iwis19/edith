# EDITH backend

A FastAPI service that holds every AI call, the search and the memory, so API keys never reach the headset. It streams Look cards written by the Card agent, picks what a Scan pins, and mints Gemini Live sessions for Ask.

## Card agent

Each Look makes two model calls:

1. The naming call streams the name, a confidence, the kind of object, a place guess and the rarity tier. The `name` event goes out as soon as the confidence arrives.
2. The fact call starts at that moment, told the name, and writes one fun fact of about 30 words. A fact that repeats one already told to the profile is rewritten once, and dropped if it repeats again.

`CARD_PROVIDER` picks who writes both calls:

| `CARD_PROVIDER` | Naming call | Fact call | Key |
| --- | --- | --- | --- |
| `openai` (default) | `gpt-4o` | `gpt-5.4-mini`, reasoning effort none | `OPENAI_API_KEY` |
| `gemini` | `gemini-3.1-flash-lite`, thinking level minimal | the same model | `GEMINI_API_KEY` |

Gemini Flash-Lite models measured on the Card agent's own prompts, with 5 headset crops each run twice (medians; Gemini's latency varied widely, with single calls up to 21 s):

| Model | Final name | Fact call | Name plus fact | Naming |
| --- | --- | --- | --- | --- |
| `gemini-3.1-flash-lite` | 2.4 s | 2.3 s | about 4.7 s | more specific ("a grey MacBook Pro laptop", "a metal Khanda pendant on a silver chain") |
| `gemini-3.5-flash-lite` | 1.3 s | 4.0 s | about 5.3 s | more generic, and more often named a person or the room instead of the framed object |

The card shows the fact, so name plus fact is the time that counts. On the same photos `openai` gave the final name in about 0.6 to 1.3 s and the fact by about 1.6 to 2.5 s.

When the phone has shared a position within the last 30 minutes (see [Phone location](#phone-location)), the naming call is also told the place and the nearest named sights, in place of the recent photos' places. The Look's `kind` and `place` are recorded with it and copied into its memory, with the phone's place preferred over the photo's guess. They are not sent in the stream.

Without the chosen provider's key, a stub agent streams canned cards on the real event schedule, so the pipe works with no keys at all. The facts told and the places seen are kept per profile in process memory: places for 6 h, and both are lost on restart.

## API

Every endpoint except `/health` needs two headers:

| Header | Value |
| --- | --- |
| `X-Edith-Token` | The device token, when `DEVICE_TOKEN` is set on the backend |
| `X-Edith-Profile` | The profile's UUID, made by the headset when a profile is created |

The phone cannot send either, so `GET /phone`, `POST /pair/claim` and `POST /location` answer without them and check their own credential instead (see [Phone location](#phone-location)).

### `GET /health`

Returns `{"status": "ok", "version": "...", "db": "connected" | "unreachable" | "disabled"}`.

### `POST /look`

A multipart form with two fields:

- `image`: the cropped JPEG, at most 2 MB and 1024 px on its longest side.
- `meta`: JSON `{"look_id": int, "source": "pinch" | "scan", "captured_at": ISO 8601}`. The image size is read from the JPEG itself.

The response is newline-delimited JSON (`application/x-ndjson`), one event per line, each with `t_ms` since the request arrived:

| Event | Fields |
| --- | --- |
| `accepted` | `look_id`, `look_ref` (the backend's ID for this Look) |
| `name` | `name`, `confidence`, `pending`: while `true`, `name` is the text written so far and `confidence` is 0; the final name follows with `pending: false` |
| `fact` | `text` |
| `rarity` | `tier`: `common`, `uncommon`, `rare`, `epic`, `legendary` or `mythic` |
| `done` | none |
| `error` | `code` (`timeout` or `agent_failed`), `message` |

The stream always starts with `accepted` and ends with `done` or `error`. In between, `fact` and `rarity` can each be missing (a repeated fact is dropped, an unreadable tier is skipped) and can come in either order, so a client must key on `type`, not position. A bad request is refused before streaming with an HTTP error and `{"error": {"code", "message"}}`. The token and the body size (the image cap plus 64 KB for the form) are checked before any of the body is read. Each finished Look is written to the `looks` table, and `/collect` turns one into a memory.

### `POST /collect`

Saves a finished Look as a memory. A multipart form with two fields:

- `image`: the JPEG to keep, under the same limits as `/look`'s image. A Look keeps no image, so the headset sends the snapshot it is pocketing.
- `meta`: JSON `{"look_ref": "<look_ref from the accepted event>", "collected_at": ISO 8601 with a timezone}`. `collected_at` is optional and becomes the memory's `created_at`; it defaults to the server time, and a time ahead of the server is capped at the server time.

The name, kind, fact, rarity tier and place are copied from the Look the backend recorded, never from the request. If the Look is still streaming, the request waits for it to be recorded (at most `LOOK_TIMEOUT_S` plus 5 s). Collecting a Look again returns the first memory and keeps its first image.

The response is `{"memory_id": int}`.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 404 | `unknown_look` | The profile has no finished Look with that `look_ref`, or the Look ended in `error` |
| 503 | `db_unavailable` | There is no database or the write failed |

### `GET /memories`

Lists the profile's memories, newest first, without image bytes. The optional `limit` query parameter is 1 to 500 and defaults to 100.

The response is `{"memories": [{"id", "look_ref", "name", "kind", "fact", "rarity", "place", "created_at"}]}`. `kind` and `place` are `null` when the Card agent gave none. Answers 503 `db_unavailable` like `/collect`.

### `GET /memories/{id}/image`

Returns the memory's JPEG (`image/jpeg`). Answers 404 `unknown_memory` for an id that is missing or belongs to another profile, and 503 `db_unavailable` like `/collect`.

### `POST /scan`

Picks the 2 or 3 things in one camera frame most worth a look, with one Gemini call that answers in JSON held to a schema. A multipart form with two fields:

- `image`: the whole camera frame as a JPEG, at most 2 MB and 1280 px on its longest side.
- `meta`: JSON `{"captured_at": ISO 8601, "session": UUID}`. The headset makes `session` once per launch.

The response is `{"scan_ref", "ms", "picks"}`. `picks` holds 0 to 3 items, most interesting first, and an empty list is a normal answer for a dull view. Each pick is:

| Field | Value |
| --- | --- |
| `name` | The thing, in at most 10 words |
| `hook` | One line saying why it is worth a look, or something to do with it |
| `point` | `[x, y]`: one spot on the thing, where its pin sits |
| `box` | `[x0, y0, x1, y1]`: a loose box around the whole thing, with `point` inside it |

Coordinates are fractions of the upright image from 0 to 1, with the origin at the top left and y running down.

The model is also told three things when they exist: the phone's place and up to 12 named sights nearby (see [Phone location](#phone-location)), the hooks this profile was already given in this `session`, and the profile's newest 50 memories, which it is asked to pass over for things not yet collected. A pick that repeats a hook for the same name within the session is dropped, and so is one whose hook opens with a generic word such as "Check" or "Look". The hooks are kept in process memory for one session per profile; a new `session` forgets the old one's. The memory lookup gets 0.6 s, after which the scan goes without it.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 503 | `scan_unavailable` | `GEMINI_API_KEY` is not set |
| 502 | `scan_failed` | The model call failed or its reply was not usable JSON |
| 504 | `scan_timeout` | The scan took longer than `SCAN_TIMEOUT_S` |

### `POST /ask/session`

Mints a Gemini Live session for one spoken question. The headset opens the websocket and talks to Gemini directly, so no audio passes through the backend and the API key never leaves it.

The body is optional JSON. `{"voice": "Kore"}` picks the voice for this session; omit it, or send no body, for `LIVE_VOICE`. Any voice Gemini Live does not list is refused with 422 `invalid_request`.

The response is `{"url", "setup", "model"}`:

- `url`: the Live websocket address with a single-use token. The token opens one session, and that session must send its first message within about a minute.
- `setup`: the JSON text the headset sends as its first message. It sets the model, spoken English, the voice, transcription of both sides (so the card can show the spoken answer) and EDITH's persona. With `LIVE_SEARCH=true` it adds Google Search.
- `model`: the Live model ID.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 503 | `ask_unavailable` | `GEMINI_API_KEY` is not set |
| 502 | `ask_failed` | Gemini refused to mint a token |

### Phone location

The headset has no GPS, so the phone that runs its hotspot pairs with the backend and shares its position. The position is kept in process memory per profile for 6 h after the last fix, is never written to the database, and is never returned by any route. The routes below give back only the place name and the nearest sight names.

The headset pairs like this:

1. `POST /pair` returns a 4-digit code and the address to open on the phone.
2. The person opens that address on the phone, types the code and taps Share location.
3. The headset polls `GET /pair/{code}` until `paired` is `true`.
4. The phone page sends its position to `POST /location` about every 10 s, or after moving 25 m.

A position becomes a place name and named sights through OpenStreetMap: Nominatim for the address and Overpass for the buildings, landmarks and campuses around the point, with no list of places kept in this repository. The backend sends each service the position rounded to about 11 m and a `User-Agent` of `EDITH/<version>` only. It asks Nominatim at most once a second, remembers each answer for 30 min per rounded point, and looks a profile up again only after it moves 75 m or its answer is 10 min old.

The Look and Ask prompts use a position only while its latest fix is at most 30 min old. `Locations.whereabouts(profile_id)` in `edith_backend/location.py` returns the place and the sights, and `describe_place` turns them into prompt text. The Scan agent uses both.

#### `POST /pair`

Needs the device token and profile headers. Returns `{"code": "4821", "expires_in_s": 600, "phone_url": "..."}`. A new call replaces the profile's unclaimed code. `phone_url` is `PHONE_LINK` when set, else this backend's `/phone`.

#### `GET /pair/{code}`

Needs the device token and profile headers. Returns `{"paired": bool, "expired": bool}`. A code that is unknown, expired or issued to another profile reads as `expired`.

#### `POST /pair/claim`

Needs no headers. The body is `{"code": "4821"}`. A code works once and lives 10 min. Returns `{"phone_token", "expires_at"}`: a signed token for that profile, valid 30 days, that works only on `POST /location`. With `DEVICE_TOKEN` set, tokens survive a restart; without it they do not.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 404 | `bad_code` | The code is wrong, used or expired |
| 429 | `too_many_tries` | 5 wrong codes from one address or 10 in total within 10 min; every claim is refused until the window passes |

#### `GET /phone`

Needs no headers. Returns the phone page, which holds no secret. It keeps the phone token in the phone's own storage.

#### `POST /location`

Needs `X-Edith-Phone` with the phone token, and no other headers. The body, at most 4 KB, is `{"lat", "lon", "accuracy_m", "taken_at"}`. Returns at once with `{"place", "sights_count", "nearest", "resolving"}`, where `place` is `null` until the first lookup finishes.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 401 | `bad_phone_token` | The token is missing, wrong or expired; the phone asks for a new code |
| 422 | `invalid_request` | The position is outside the earth's range |

#### `GET /location`

Needs the device token and profile headers. Returns the same summary as `POST /location` when a position is kept for the profile, so the headset can skip pairing, else 404 `no_location`.

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

Copy `.env.example` to `.env` and set the key for `CARD_PROVIDER`. With no `DATABASE_URL` the app runs without a database. Model IDs, the embedding size and the stub timings are settings in `edith_backend/config.py`.

Ask settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `LIVE_MODEL` | `gemini-3.8-live` | The Gemini Live model Ask talks to |
| `LIVE_VOICE` | `Kore` | The prebuilt Live voice. Gemini silently falls back to its default for a name it does not know, so the backend accepts only listed names |
| `LIVE_SEARCH` | `false` | Adds Google Search to the session. A free-tier key refuses the session with it on |

Scan settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `SCAN_MODEL` | `gemini-3.5-flash` | The Gemini model that picks what to pin |
| `SCAN_THINKING_LEVEL` | `minimal` | The model's thinking level; more thinking makes pins later |
| `SCAN_TIMEOUT_S` | `7` | The time one scan may take. The headset waits 8 s |

Phone location settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `PHONE_LINK` | empty | The address the headset shows for the phone page. Empty shows this backend's own `/phone`. Set a short link that redirects there when the domain is too long to type |

To see a Look stream end to end against a running backend:

```
uv run python -m tools.stream_check http://127.0.0.1:8000 photo.jpg
```

It prints when each event reaches the client and the server's `t_ms` beside it.

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
2. Set `DATABASE_URL`, `DEVICE_TOKEN` and the key for `CARD_PROVIDER` (`OPENAI_API_KEY` by default, or `GEMINI_API_KEY` with `CARD_PROVIDER=gemini`) on the service. The image sets `PUBLIC_SERVER=1`, so the app refuses to start without `DEVICE_TOKEN`.
3. Keep one replica: the facts told, the places seen, the scan hooks and the phone's location are held in process memory.

## Layout

- `edith_backend/app.py`: routes, header and upload checks, the NDJSON stream.
- `edith_backend/agents/ask.py`: the Ask persona prompt, the token request and the session setup.
- `edith_backend/agents/card.py`: the `CardAgent` protocol, the stub, and `make_card_agent`, which picks the agent from `CARD_PROVIDER`.
- `edith_backend/agents/card_model.py`: the two-call Card agent, its line parser, and the fact and place histories.
- `edith_backend/agents/scan.py`: the Scan agent with its prompt, the repair of the model's picks, the Gemini call and the hook memory.
- `edith_backend/agents/writers.py`: the OpenAI and Gemini streaming writers.
- `edith_backend/events.py`: the stream's event types, the Look metadata and the details recorded but not streamed.
- `edith_backend/pairing.py`: pairing codes with their throttle, and the signed phone tokens.
- `edith_backend/location.py`: the phone's position per profile, kept in memory, and the place description for prompts.
- `edith_backend/resolve.py`: Nominatim and Overpass lookups with the cache and the rate limit.
- `edith_backend/static/phone.html`: the page the phone opens.
- `edith_backend/db.py`: the pooled Postgres store, or a no-op store with no database.
- `edith_backend/migrate.py` and `edith_backend/migrations/`: the schema, including the `memories` table that `/collect` fills.
- `tools/stream_check.py`: prints the arrival time of each event of one Look.
