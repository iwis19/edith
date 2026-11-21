# EDITH backend

A FastAPI service that holds every AI call, the search and the memory, so API keys never reach the headset. It streams Look cards written by the Card agent, picks what a Scan pins, and mints Gemini Live sessions for Ask.

## Card agent

Each Look makes two model calls:

1. The naming call streams the name, a confidence, the kind of object, a place guess and the rarity tier. The `name` event goes out as soon as the confidence arrives.
2. The fact call starts at that moment, told the name, and writes one fun fact of about 30 words. A fact that repeats one already told to the profile is rewritten once, told every fact already told for the Look's kind. If the rewrite repeats too, the card shows the fact for that kind told longest ago and counts it as told again, so a card with a fact never goes out without one.

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

When the phone has shared a position within the last 30 minutes (see [Phone location](#phone-location)), the naming call is also told the place and the nearest named sights, beside the recent photos' places. The Look's `kind`, `place` and `photo_place` (the place the photo itself showed) are recorded with it and copied into its memory, with the phone's place preferred over the photo's guess. They are not sent in the stream.

Without the chosen provider's key, a stub agent streams canned cards on the real event schedule, so the pipe works with no keys at all. The places photos showed are kept per profile for 6 h. With a database they are loaded back from `looks.photo_place` at startup, so they survive a restart. The last 50 facts told to each profile are kept in memory too, and also in the `told_facts` table when there is a database, so they survive a restart.

### Model trade-offs

Measured on 68 photos, one run each, so differences of about 3 are noise. Names were graded blind against labels from a separate labeller, and shown facts were checked by a web-searching model. Times are medians.

**Naming call**

| Model | Specific | Generic | Wrong | First word |
| --- | --- | --- | --- | --- |
| `gpt-4o` | 23 | 34 | 11 | 0.63 s |
| `gpt-5.4-mini`, one long card prompt | 9 | 50 | 9 | 0.65 s |
| Gemini Flash-Lite | 23 | 29 | 14 | 1.2 to 1.6 s |

- `gpt-4o` names as many objects specifically as Gemini Flash-Lite, with fewer wrong names. Its wrong names are mostly near misses, such as a neighbouring model of the same product or a too-narrow guess.
- `gpt-5.4-mini` names few objects wrongly only because it names most of them generically. Asked plainly, without the long card prompt, it named some objects specifically that the long prompt left generic, so its generic names come from the prompt as much as the model.
- Gemini Flash-Lite's wrong names are invented specifics, such as a made-up title, where OpenAI's wrong names are more often the wrong object. It is about twice as slow to the first word, and the free tier allows 15 requests a minute.
- Neither provider's confidence separates right names from wrong ones.

**Fact call**

| Model | Beats the other on a blind judge | Median length | Median time | Shown facts false |
| --- | --- | --- | --- | --- |
| `gpt-5.4-mini`, effort none | 42 to 26 | 30 words | 1.82 s | similar to `gpt-4o` |
| `gpt-4o` | 26 to 42 | 29 words | 1.90 s | similar to `gpt-5.4-mini` |

- Written in one call with the name and passed through a second-model check, `gpt-4o`'s shown facts had 3 of 68 false, against 10 of 65 for `gpt-5.4-mini`. As a separate call told the name, the two have a similar share of stretched details, and `gpt-5.4-mini` costs less per Look.
- A web check of `gpt-4o`'s separate-call facts found about half embellished and about 1 in 17 invented. The push for specific details causes this, so a fact is a fun fact, not a checked one.
- Gemini Flash-Lite's facts arrive at a median of 2.4 to 3.0 s, with 12 of 59 false, against 2.1 s for OpenAI in a single call.
- Larger Gemini models think for seconds before writing, which is too slow for a Look.

`gpt-4o` is reported to leave the OpenAI API on 2027-01-14, with `gpt-6-luna` named as its replacement. Measure the replacement on the same photos before then, and set it with `OPENAI_NAME_MODEL`.

**Two calls rather than one**

- Separate calls let each job use the model that suits it: `gpt-4o` names, `gpt-5.4-mini` writes the fact. The fact call carries its own prompt, the facts already told to the profile and the repeat check. The cost is a second request per Look.
- The fact call starts only once the name is final, at about 0.76 s. Started blind beside the naming call, it picked a different object in the crop on 16 of 68 photos, so the fact described a different object from the name. Told the name, it did so on none. The cost is the fact arriving after the name, not with it.
- A failed fact call never costs the card its name or rarity; the card goes out without a fact.

The tests in `tests/test_card_model.py` check the parsing, the repeat check and the call ordering against fake writers, so they pass without a key and say nothing about the quality or speed of any model. To measure a setup, set `CARD_PROVIDER`, the key and the model settings, run the backend, and send the same photos through `tools.stream_check`, which prints when the name, fact and rarity events arrive.

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
| `fact` | `text`, `sources` (`title`, `url`) |
| `rarity` | `tier`: `common`, `uncommon`, `rare`, `epic`, `legendary` or `mythic` |
| `hunt` | A Look ticked a hunt target: `index`, `text`, `found` (targets found so far), `total` (5), `status` (`active` or `found_all`). Only sent during a hunt, see [Bounty](#bounty) |
| `done` | none |
| `error` | `code` (`timeout` or `agent_failed`), `message` |

The stream always starts with `accepted` and ends with `done` or `error`. In between, `fact` and `rarity` can each be missing (a repeated fact is dropped, an unreadable tier is skipped) and can come in either order, so a client must key on `type`, not position. A bad request is refused before streaming with an HTTP error and `{"error": {"code", "message"}}`. The token and the body size (the image cap plus 64 KB for the form) are checked before any of the body is read. Each finished Look is written to the `looks` table, and `/collect` turns one into a memory.

### `POST /collect`

Saves a finished Look as a memory. A multipart form with two fields:

- `image`: the JPEG to keep, under the same limits as `/look`'s image. A Look keeps no image, so the headset sends the snapshot it is pocketing.
- `meta`: JSON `{"look_ref": "<look_ref from the accepted event>", "collected_at": ISO 8601 with a timezone}`. `collected_at` is optional and becomes the memory's `created_at`; it defaults to the server time, and a time ahead of the server is capped at the server time.

The name, kind, fact, sources, rarity tier and place are copied from the Look the backend recorded, never from the request. If the Look is still streaming, the request waits for it to be recorded (at most `LOOK_TIMEOUT_S` plus 5 s). Collecting a Look again returns the first memory and keeps its first image.

The response is `{"memory_id": int}`.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 404 | `unknown_look` | The profile has no finished Look with that `look_ref`, or the Look ended in `error` |
| 503 | `db_unavailable` | There is no database or the write failed |

### `GET /memories`

Lists the profile's memories, newest first, without image bytes. The optional `limit` query parameter is 1 to 500 and defaults to 100.

The response is `{"memories": [{"id", "look_ref", "name", "kind", "fact", "sources", "rarity", "place", "created_at"}]}`. `kind` and `place` are `null` when the Card agent gave none. Answers 503 `db_unavailable` like `/collect`.

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
| `target` | The `index` of the open hunt target the thing clearly fits, or `null`. Always `null` outside a hunt |

Coordinates are fractions of the upright image from 0 to 1, with the origin at the top left and y running down.

The model is also told three things when they exist: the phone's place and up to 12 named sights nearby (see [Phone location](#phone-location)), the hooks this profile was already given in this `session`, and the profile's newest 50 memories, which it is asked to pass over for things not yet collected. A pick that repeats a hook for the same name within the session is dropped, and so is one whose hook opens with a generic word such as "Check" or "Look". The hooks are kept in process memory for one session per profile; a new `session` forgets the old one's. The memory lookup gets 0.6 s, after which the scan goes without it.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 503 | `scan_unavailable` | `GEMINI_API_KEY` is not set |
| 502 | `scan_failed` | The model call failed or its reply was not usable JSON |
| 504 | `scan_timeout` | The scan took longer than `SCAN_TIMEOUT_S` |

### `POST /ask/session`

Mints a Gemini Live session for one spoken question. The headset opens the websocket and talks to Gemini directly, so no audio passes through the backend and the API key never leaves it.

The body is optional JSON. `{"voice": "Kore"}` picks the voice for this session; omit it, or send no body, for `LIVE_VOICE`. Any voice Gemini Live does not list is refused with 422 `invalid_request`. `{"hint": {"name": "...", "hook": "..."}}` names the Scan pin nearest the person's line of sight, and the persona then takes "this" and "it" to mean that pin (name at most 200 characters, hook at most 300).

The response is `{"url", "setup", "model"}`:

- `url`: the Live websocket address with a single-use token. The token opens one session, and that session must send its first message within about a minute.
- `setup`: the JSON text the headset sends as its first message. It sets the model, spoken English, the voice, transcription of both sides (so the card can show the spoken answer) and EDITH's persona. With `LIVE_SEARCH=true` it adds the `googleSearch` tool and a persona line that limits searching to questions that need current or factual web information. The headset reads the sources from `serverContent.groundingMetadata.groundingChunks[].web` and shows up to three site names under the answer.
- `model`: the Live model ID.

The setup always declares two function tools, `start_hunt` (optional `interests`: an array of strings) and `stop_hunt` (no arguments), and the persona tells the model to call them when the person asks for a scavenger hunt or asks to stop one. The headset runs a call by sending `POST /hunt` (with `{"interests": [...]}` when given) or `POST /hunt/stop`, and answers the call with the response JSON as the `toolResponse` (`{"functionResponses": [{"id": <the call's id>, "name": <the call's name>, "response": <the HuntState>}]}`). An error body from those routes goes back the same way. While the profile has an active hunt, the persona also lists the open targets, the found ones and the time left, so "what's left?" is answered from the session.

| Status | `error.code` | Meaning |
| --- | --- | --- |
| 503 | `ask_unavailable` | `GEMINI_API_KEY` is not set |
| 502 | `ask_failed` | Gemini refused to mint a token |

To time a spoken-question turn with search on and off (five runs each, a weather question and a plain one), run in `backend/` with `GEMINI_API_KEY` set:

```
uv run python -m tools.search_latency --mode both --runs 5
```

It sends each question as realtime text and prints the milliseconds to the first answer audio, the end of the turn, and whether grounding came back.

### Bounty

A scavenger hunt: 5 targets on one clock (`HUNT_SECONDS`, 300 by default), ticked off by Looks. The backend holds each profile's active hunt, so the headset never sends targets; it starts and stops the hunt and reads each find from the `hunt` event on a Look. A profile has at most one active hunt, kept in process memory and written through to Postgres (`hunts` and `hunt_targets`), so a restart picks a running hunt back up. Without a database the hunt still runs and `GET /hunts` answers 503 `db_unavailable`.

A hunt has one of four statuses, and `timeout` is worked out from `ends_at` and the clock, never stored:

| `status` | Meaning |
| --- | --- |
| `active` | Running |
| `found_all` | All 5 found |
| `stopped` | Ended by `POST /hunt/stop`, or replaced by a new `POST /hunt` |
| `timeout` | `ends_at` passed first |

Every route below needs the same two headers as the others. `HuntState` is:

```json
{
  "hunt_ref": "32 hex characters",
  "status": "active",
  "started_at": "2026-11-21T10:00:00Z",
  "ends_at": "2026-11-21T10:05:00Z",
  "ended_at": null,
  "remaining_s": 300,
  "place": "Waterloo, Ontario, Canada",
  "found": 0,
  "targets": [
    {"index": 0, "text": "something red", "tier": null, "found": false, "found_at": null, "look_ref": null, "memory_id": null},
    {"index": 3, "text": "something Uncommon or better", "tier": "uncommon", "found": false, "found_at": null, "look_ref": null, "memory_id": null}
  ]
}
```

`remaining_s` is 0 once the hunt is over. `ended_at` is set for `found_all` and `stopped`, and is `null` for `timeout`; the time taken for a timeout is `ends_at - started_at`. `place` is the phone's place name when there was one at the start. `tier` is set only on a rarity target, which any Look of that tier or better ticks. `look_ref` is the Look that found the target. `memory_id` is filled only by `GET /hunts`, and only once that Look was collected.

#### `POST /hunt`

Starts a hunt and returns its `HuntState`. The body is optional JSON `{"interests": ["bikes", "birds"]}`. A hunt still running for the profile ends as `stopped`. One fast Gemini call (`HUNT_MODEL`, default `GEMINI_CARD_MODEL`) writes the targets from the phone's place and the interests: 3 or 4 themes a photo of one object can show, and 1 or 2 rarity targets of tier `uncommon` or `rare` (never higher). The reply is repaired to exactly 5 distinct targets. Without a Gemini key, or when the call fails or takes longer than `HUNT_TARGETS_TIMEOUT_S` (4 s), the targets come from a built-in pool, so a hunt always starts.

#### `GET /hunt`

The active hunt's `HuntState`, or 404 `no_hunt` when there is none, including after a timeout.

#### `POST /hunt/stop`

Ends the active hunt as `stopped` and returns its final `HuntState`, or 404 `no_hunt`.

#### `GET /hunts`

`?limit=` (1 to 200, default 50). Returns `{"hunts": [HuntState, ...]}`, newest first, past and current, with `memory_id` on each found target whose Look was collected through `/collect`. 503 `db_unavailable` without a database.

#### Looks and scans during a hunt

While a hunt is active, `/look` and `/scan` pass its open targets to the model, and the headset sends nothing extra.

- The naming call is asked for a sixth line, `HUNT`, with the number of the open target the framed object clearly fits. The first name still goes out as soon as it is written.
- Once the final name and the rarity are known, the backend ticks at most one target for the Look and streams a `hunt` event before the `fact`. The pick of the card call counts if it names an open theme target. Otherwise the lowest open rarity target whose tier the Look's rarity meets or beats is ticked. A Look whose name starts with "Maybe", or whose `confidence` is below 0.5 (`MIN_TICK_CONFIDENCE`), ticks nothing. When the 5th target is found the event carries `status: "found_all"` and the hunt is over.
- `/scan` asks the model for an optional `target` per pick, returned on the pick as the target's `index`.
- A hunt that has timed out or ended sends no targets and ticks nothing.

### Phone location

The headset has no GPS, so the phone that runs its hotspot pairs with the backend and shares its position. The position is kept in process memory per profile for 6 h after the last fix, is never written to the database, and is never returned by any route. The routes below give back only the place name and the nearest sight names.

The headset pairs like this:

1. `POST /pair` returns a 4-digit code and the address to open on the phone.
2. The person opens that address on the phone, types the code and taps Share location. A phone that is already paired shows the code box under "Enter a new code"; claiming a code replaces the phone's stored token.
3. The headset polls `GET /pair/{code}` until `paired` is `true`.
4. The phone page sends its position to `POST /location` about every 10 s, or after moving 25 m.

A position becomes a place name and named sights through OpenStreetMap: Nominatim for the address and Overpass for the buildings, landmarks and campuses around the point, with no list of places kept in this repository. The backend sends each service the position rounded to about 11 m and a `User-Agent` of `EDITH/<version>` only. It asks Nominatim at most once a second, remembers each answer for 30 min per rounded point, and looks a profile up again only after it moves 75 m or its answer is 10 min old.

The Look and Ask prompts use a position only while its latest fix is at most 30 min old (`FRESH_S`). That outlasts `PHONE_SILENT_S`: a person who stopped somewhere is still there minutes later. `Locations.whereabouts(profile_id)` in `edith_backend/location.py` returns the place and the sights, and `describe_place` turns them into prompt text. The Scan agent uses both.

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

Needs the device token and profile headers. Returns the same summary as `POST /location` while the phone's latest fix is at most 30 s old (`PHONE_SILENT_S`), so the headset can skip pairing, else 404 `no_location`. The phone page re-sends every 10 s, so a phone that closed the page reads as gone after three missed re-sends, and the headset, which checks every 30 s and locks after three misses, locks about 1.5 min later. A backend the headset cannot reach never locks it.

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
| `LIVE_SEARCH` | `false` | Adds Google Search to the session. A free-tier key refuses the session with it on. Off until `tools/search_latency.py` shows the time to first audio stays acceptable |

Scan settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `SCAN_MODEL` | `gemini-3.5-flash-lite` | The Gemini model that picks what to pin |
| `SCAN_BACKUP_MODEL` | `gpt-4o` | The OpenAI model asked as well when the Gemini call fails or is slow; needs `OPENAI_API_KEY` |
| `SCAN_BACKUP_AFTER_S` | `2` | How long the Gemini call runs alone before the backup is asked too; 0 turns the backup off |
| `SCAN_THINKING_LEVEL` | `minimal` | The model's thinking level; more thinking makes pins later |
| `SCAN_TIMEOUT_S` | `7` | The time one scan may take. The headset waits 8 s |
| `HUNT_SECONDS` | `300` | The length of a Bounty hunt's clock |
| `HUNT_MODEL` | empty | The Gemini model that writes a hunt's targets. Empty uses `GEMINI_CARD_MODEL` |
| `HUNT_TARGETS_TIMEOUT_S` | `4` | How long the target call may take before the built-in targets are used |

Kind matching settings:

| Setting | Default | Meaning |
| --- | --- | --- |
| `EMBEDDING_MODEL` | `gemini-embedding-2` | The Gemini model that embeds a Look's kind of object, so "mug" and "coffee cup" count as one kind. Needs `GEMINI_API_KEY`; without it kinds match by a shared word |
| `EMBEDDING_DIMENSIONS` | `1536` | The embedding size. It must equal the `vector(1536)` columns in the migrations |
| `KIND_SIMILARITY` | `0.8` | The cosine similarity at which two kinds count as one |
| `EMBEDDING_TIMEOUT_S` | `0.3` | The time the embedding call may take. It starts after the name has gone out and runs beside the fact call, and a call that runs over is dropped, so the kinds match by a shared word for that Look |

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
3. Keep one replica: the scan hooks and the phone's location are held in process memory, and each replica keeps its own copy of the places seen.

## Layout

- `edith_backend/app.py`: routes, header and upload checks, the NDJSON stream.
- `edith_backend/agents/ask.py`: the Ask persona prompt, the token request and the session setup.
- `edith_backend/agents/card.py`: the `CardAgent` protocol, the stub, and `make_card_agent`, which picks the agent from `CARD_PROVIDER`.
- `edith_backend/agents/card_model.py`: the two-call Card agent, its line parser, and the fact and place histories.
- `edith_backend/agents/embeddings.py`: the Gemini embedding client for kinds of object, and cosine similarity.
- `edith_backend/agents/scan.py`: the Scan agent with its prompt, the repair of the model's picks, the Gemini call with its OpenAI backup, and the hook memory.
- `edith_backend/agents/writers.py`: the OpenAI and Gemini streaming writers.
- `edith_backend/events.py`: the stream's event types, the Look metadata and the details recorded but not streamed.
- `edith_backend/pairing.py`: pairing codes with their throttle, and the signed phone tokens.
- `edith_backend/location.py`: the phone's position per profile, kept in memory, and the place description for prompts.
- `edith_backend/resolve.py`: Nominatim and Overpass lookups with the cache and the rate limit.
- `edith_backend/static/phone.html`: the page the phone opens.
- `edith_backend/db.py`: the pooled Postgres store, or a no-op store with no database.
- `edith_backend/migrate.py` and `edith_backend/migrations/`: the schema, including the `memories` table that `/collect` fills and the `told_facts` table that keeps the facts told to each profile, with the embedding of their kind. `memories.embedding` is not written.
- `tools/stream_check.py`: prints the arrival time of each event of one Look.
- `tools/rarity_eval.py`: compares the naming prompt's rarity tiers at a git ref with the working tree's on a folder of cropped Look photos.
