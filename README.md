# EDITH 🧭

Turning the places you walk through into side quests with a mixed-reality companion that looks, dares, remembers, and guides!

---

## Repository Note

Built at **Hack Western 13** for Meta Quest 3 / 3S:
- Best Use of Gemini API

Check out the Devpost submission: [click](https://devpost.com/software/edith-w3m9c8?ref_content=user-portfolio&ref_feature=in_progress)

---

## Key Features

1. **Hands-Only Mixed Reality Play**
   - **Scan**: hold both palms out to drop pins on up to 3 interesting objects
   - **Look**: pinch and drag a box to get snapshot cards with a name, a rarity, and a fun fact
   - **Collect**: close your fist on a card and pull to your chest to save it
   - **Scrapbook**: hold both palms up like an open book to browse

2. **Streaming Vision Agents**
   - Card agent splits each Look into a naming call and a fact call, so each job gets the model that suits it
   - Fun facts are never repeated for the same *kind* of object, matched by Gemini embeddings
   - Six rarity tiers from Common to Mythic
   - Scan races Gemini Flash-Lite against a GPT-4o backup after 2s
   - The headset saves camera pose and depth at capture time

3. **Extra Implementations**
   - Optional Google Search grounding for current questions
   - The headset has no compass, so north is worked out from the phone's heading and GPS course paired with head yaw

4. **Persistent Memory + Privacy**
   - Tiger Data (Tiger Cloud Postgres) with pgvector stores memories, told facts, and embeddings
   - Trails use a TimescaleDB hypertable and continuous aggregates
   - A phone shares location through a 4-digit code, since the Quest has no GPS
   - Every AI call sits behind the backend, so API keys never touch the headset

---

## Tech Stack

- **C# / Unity 6**
- **Meta XR SDK** + **Passthrough Camera API**
- **Python**
- **FastAPI**
- **Gemini** (Flash-Lite, Live, Embeddings)
- **PostgreSQL** + **pgvector** + **TimescaleDB** (Tiger Data)
- **OpenStreetMap** (Nominatim, Overpass, Valhalla, tiles)
- **Railway**
- **Docker**

---

## Try It Out

EDITH runs on a developer-mode Meta Quest 3 or 3S paired with a phone, and the backend needs Gemini/OpenAI keys plus a Postgres database. If you have the hardware, the [backend guide](backend/README.md) and [headset guide](headset/README.md) cover the full setup.

---

## Project Structure

```text
edith/
├── backend/
│   ├── edith_backend/
│   │   ├── agents/               # Card, Scan, Ask agents + embeddings
│   │   ├── migrations/           # Memories, looks, told facts
│   │   ├── migrations_trails/    # Timescale trail points + cells
│   │   ├── static/phone.html     # Phone pairing + location page
│   │   ├── app.py                # FastAPI routes
│   │   ├── location.py           # Phone fixes, place context
│   │   ├── navigate.py           # find_place targets
│   │   ├── route.py              # Valhalla walking routes
│   │   ├── trails.py             # Walked / unexplored areas
│   │   └── maps.py               # OSM tile stitching for the scrapbook
│   ├── tests/
│   ├── tools/                    # Latency + rarity evals
│   └── Dockerfile
│
├── headset/
│   └── Assets/Edith/
│       ├── Runtime/
│       │   ├── Hands/            # Hand tracking + gesture gates
│       │   ├── Pinch/            # Look box drawing
│       │   ├── Grab/             # Fist-pull collect
│       │   ├── Look/             # Snapshot cards + NDJSON stream
│       │   ├── Scan/             # Pins placed with depth
│       │   ├── Ask/              # Gemini Live voice + answer orb
│       │   ├── Navigation/       # Finger guns + blue arrow
│       │   ├── Scrapbook/        # Memory deck
│       │   ├── Pairing/          # Phone pairing flow
│       │   └── Occlusion/        # Hands hide cards behind them
│       └── Editor/               # One-click project setup + APK build
│
├── PRD.md
└── LICENSE
```

---

## Interaction Workflow

```text
Palms Out (Scan)
    ▼
Passthrough Frame + Pose + Depth
    ▼
Scan Agent (Gemini ⟷ GPT-4o race)
    ▼
Pins with Dares
    ▼
Pinch & Drag (Look)
    ▼
Card Agent
   ╱     ╲
 Name    Fun Fact
 Rarity  (no repeats)
   ╲     ╱
  Snapshot Card
   ╱     ╲
 Ask      Fist Pull
 (Voice)  (Collect)
  ▼         ▼
Gemini    Postgres + pgvector
Live          ▼
            Scrapbook
```

Each Look streams its name, fact and rarity as separate events, so the card fills in piece by piece, and collected memories keep only what the card shows: photo, name, fact, tier, place, and date.

---

## Lessons Learned

- We can hide a bit of model latency by streaming partial results and UI animations
- Benchmarking vision models on headset crops with photos from everywhere around the world
- Building location-aware features while keeping the user's position out of storage and logs
- Developing on Quest as a team SUCKS due to Android building tools and Unity build sessions only happening 1 at a time...
