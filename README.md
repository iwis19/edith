# EDITH

**Discover the side quests hiding in the places you walk through.** Built at Hack Western 13 for Meta Quest 3 and 3S.

Imagine walking past a mural on campus. You raise both palms, and a pin appears on the mural with a dare: *Can you find the fox hiding in the branches?* Suddenly you have a reason to stop. When you spot it, draw a frame around it with your fingers, then pull the photo card to your chest to keep it.

That’s the idea behind EDITH. The headset looks at your surroundings and comes up with something to try right there. The mural and fox are one example. Each scan uses what the camera can actually see.

## Playing with your hands

| Action | Gesture | What happens |
| --- | --- | --- |
| **Scan** | Hold both open palms facing outward for half a second. | Pins appear on interesting objects. A pin might challenge you to spot a detail, guess something you can check, or copy a statue’s pose. |
| **Look** | Pinch, drag a box around a find, and release. | A cropped photo hangs where you drew the frame. Its name, rarity, and a short generated fact fill in as they arrive. |
| **Ask** | Look at a find and tap your thumb against the side of your index finger. | Ask a question out loud. EDITH uses the nearby pin or photo as context and speaks the answer. |
| **Collect** | Close your fist on a card and pull it toward your chest. | The find goes into your scrapbook. The sound you hear changes with its rarity. |
| **Revisit** | Turn both palms up to open the scrapbook. | Browse the things you kept and see where you found them. |

## From camera frame to playable clue

Scan sends a single passthrough frame to the Python backend. The vision model picks up to three objects and returns a position, a bounding box, and a short prompt for each. It looks for something a player can do in under a minute. When an object offers no good dare, it can suggest something to try or share a fact instead.

The tricky part is that you can turn your head while the model is answering. The Unity client saves the camera pose and depth information at capture time, then uses them to place the returning pins in the room. A clue about the mural stays with the mural as you look around.

Look takes a tighter crop of the object you frame. The backend streams the identification and fact into a card. Collect saves your chosen image and its details to PostgreSQL. A paired phone supplies place context, and Gemini Live handles spoken follow-up questions.

**In development — Bounty.** A timed scavenger-hunt mode gives you five targets in five minutes. You interpret a clue such as *something that opens*, choose a real object that fits, and frame it to tick off the target. The backend and headset implementations are in [PR #19](https://github.com/iwis19/westernhackers/pull/19) and [PR #25](https://github.com/iwis19/westernhackers/pull/25).

## Run it

### Backend

Install `uv`, then run the following from the repository root:

```sh
cd backend
cp .env.example .env
uv sync
```

Set `GEMINI_API_KEY` in `.env` for live Scan and Ask. For Look cards, set `CARD_PROVIDER=gemini` to use the same key, or supply `OPENAI_API_KEY` for the default provider. To keep finds in the scrapbook, set `DATABASE_URL` to a PostgreSQL database with pgvector and run `uv run python -m edith_backend.migrate`.

```sh
uv run uvicorn edith_backend.app:app --host 127.0.0.1 --port 8000 --loop asyncio:SelectorEventLoop
```

Open `http://127.0.0.1:8000/health` to check the service. The [backend guide](backend/README.md) covers HTTPS deployment, database setup, and running the tests with `uv run pytest`.

### Headset

1. Make the backend available at an HTTPS address reachable from the Quest and phone. Set `EDITH_BACKEND_URL` and the matching `DEVICE_TOKEN` in `backend/.env` before configuring Unity.
2. Open `headset/` in Unity **6000.6.9f1** with Android Build Support, OpenJDK, and Android SDK & NDK Tools installed.
3. Run **EDITH → Configure Project**, let Unity finish recompiling, then select **EDITH → Build APK**. Install `headset/Builds/edith.apk` on a developer-mode Quest 3 or 3S.
4. Follow the welcome flow, grant the headset permissions, then enter the pairing code on your phone and tap **Share location**. Raise both palms toward something worth exploring.

The [headset guide](headset/README.md) has the exact install commands, gesture checks, and Unity EditMode test instructions.

## License

[MIT](LICENSE)
