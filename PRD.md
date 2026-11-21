# EDITH: Product Requirements

## Overview

EDITH is a hands-only Quest 3/3S mixed-reality companion that makes whatever you look at entertaining, then remembers it for you. Entertainment comes first; the memory layer is what makes it more than a toy.

**Goals**

- Every interaction is fun on its own: a playful take, a rarity reveal, a déjà vu callback, pins on the most interesting things in sight.
- Everything you save is easy to find again by meaning, with when it happened.
- No controllers: every action works with bare hands, and voice is a backup.

**Out of scope for now:** multiplayer, a walkable gallery.

## Core loop

| Step | Trigger | What you get | Saved? |
| --- | --- | --- | --- |
| Look | Pinch empty space, drag a box around an object, let go | A snapshot card pinned where you took it: what it is, a fun fact you haven't heard for that kind of object, a rarity glow | No |
| Collect | Close a fist on the snapshot and pull it in toward your chest | A memory: cropped image, name, fun fact, rarity, place and date | Yes |
| Ask | Thumb tap on the side of the index finger, then speak, about anything | A spoken answer typed out in a two-line caption beside a small orb, which perches on the snapshot asked about or rests low in your view, shows what was heard, and fades when the answer is over | No |
| Scan | Palms-out pose, aimed at what's in front of you | Pins on the 2 or 3 things in sight that make the most fun dares, each with a short dare to do on the spot; pinch one to open it as a snapshot card | Only cards you collect from it |
| Remember | Open the Scrapbook with both palms held up like an open book, or ask "what did I find today?" | Memories found by meaning, with when and where | Not applicable |

## Features

### Welcome

A short guided start on first launch: EDITH introduces itself, you pick or say a few interests (menu, typing or voice), and it walks you through one practice Look. It also includes pairing the phone for location.

### Look result

- **Identify:** name the object as specifically as the photo allows, with a confidence. When unsure, say so playfully and offer a best guess; never invent a confident wrong name.
- **Fun fact:** one short, true, surprising fact per Look, not repeated for that kind of object (kinds matched by embedding, so "mug" and "coffee cup" are one kind). When no fresh fact is left, EDITH owns it and reuses the one told longest ago. Fun wins over a strict check.
- **Snapshot:** on release the box is filled with the cropped photo, where and how big you drew it, and it stays there. While you pinch and drag, a glowing cyan rounded frame with white corner brackets marks the box. On release it becomes the card: a liquid-glass panel with a thick glowing border, a thin inner frame and white corner brackets around the photo, then a divider with a dot, the name in bold and the fun fact under it, typed out in place (the name alone when there is no fact). the rarity colour tints its outline and glow, and the text stays ice white. A photo narrower than a card's minimum width (about 14.5 degrees) is shown widened about its centre, while the backend reads only the framed box.
- Up to four snapshots in the room; a fifth removes the oldest, and a new box erases the ones it covers.
- A quick pinch in line with a photo clears it from any distance.
- Real hands and objects in front of a snapshot hide it.

### EDITH's voice

Multiple voices to choose from on welcome page, light/calm/enthusiastic, with no narrator personas. Ask is the only part that speaks; Look cards are text. The voice is a Gemini Live voice setting on the backend, so it changes with no headset rebuild.

### Rarity

How rare the thing is in the world, judged on the first Look, with no collection needed. It measures how special the thing is, not how often people near it see it.

| Tier | Colour | What earns it |
| --- | --- | --- |
| Common | Grey | Found almost anywhere |
| Uncommon | Green | Typical of a region or a niche |
| Rare | Blue | Notable in its place, or a genuinely unusual find |
| Epic | Purple | A must-see of its city, or a famous collectible |
| Legendary | Gold | Famous worldwide |
| Mythic | Red until the UI pass | One of the world's few most famous sights or objects |

- The card call that names the object also returns the tier. It judges the specific thing seen, not its category, and picks the lower tier when torn.
- The snapshot card's frame glows the tier colour during a Look, before you collect.
- Each tier has its own sound on collect.
- A memory keeps the tier it had when collected.
- When something would come in as Common, EDITH nudges you to find something new.
- The Scrapbook can filter and sort by tier and shows a counter per tier.

### Déjà vu

Frame something close to an old memory and EDITH calls it back with a ghost of the old card. Needs embeddings of saved memories.

### Scan

Hold up your palms and EDITH points out what's worth a look in front of you. For exploring: no game and no points by default.

1. **Start:** both palms open and facing away for half a second, or say "EDITH, scan." A ring fills as you hold, a chime starts the scan, and the headset grabs one camera frame.
2. **Pick:** one vision call finds the 2 or 3 most interesting things and skips the dull ones. Each gets a point, a loose box and a hook.
3. **Pin:** a pin sits on each object, placed with depth, carrying the hook. Something beyond depth's reach gets a pin that points the way, marked "(far)".
4. **Open:** pinch a pin and it becomes a normal snapshot card through the usual Look.

The hook is not a fact: it is why the thing is worth a look, or something to do with it.

- Pins appear within about 2 s of the gesture.
- A new scan replaces the last one's pins. An opened pin's card counts toward the four snapshots.
- The same object never gets the same hook twice in one session. EDITH prefers things you haven't collected.
- Ask's hint is the pin or card nearest your line of sight.
- A higher chime means pins landed; a low tone and a line of text mean none did, or EDITH could not be reached.
- Recentring the view clears the pins.
- Later, maybe: frames gathered while you look around, so one scan can pin more.

### Scrapbook

A deck of memory cards, placed 0.7 m in front of where you look when it opens, at eye level and fixed there in the room. The middle card is full size and is a snapshot's card: the photo in a rarity-coloured border with the fun fact under it. Two smaller cards fade back on each side. Thumb swipe or a sideways pinch-drag scrolls, and a pinch on a side card brings it to the middle. A pinch chooses at the point where it was pressed unless it moves 6 cm or more sideways. A row of six rarity chips under the deck, Common to Mythic, each with its count and dimmed when empty, narrows the deck to one tier. A pinch on the chip in use shows every card again. The deck runs from Common to Mythic, newest first within a tier, and opens at its first card. Letting go of a drag lands on the card the drag was heading to: movement in the last 0.12 s before the release is ignored, and a small reversal at the end does not turn the deck back. Voice search lights up matching cards, using embeddings over labels and facts.

A memory keeps only what its card shows: the photo, name, fun fact, tier, place, the local date it was collected (no time of day), the place's centre point for the map, and the search embedding. Your own position is never stored.

### Where you are: the phone's location

The headset has no GPS, so the phone that runs the hotspot shares its position with the backend.

- **Pairing:** the headset shows a 4-digit code and a short link; the person opens the page on the phone, types the code and taps Share location.
- **Lock:** until a phone is paired, every feature stays off. If the phone disconnects later (the backend stops hearing its position), the headset locks every feature again and shows the pairing panel, and nothing works until the phone is paired once more.
- **What it feeds:** the place and the nearest named sights go to the Scan, the Look's place context and Ask. With no fix, the Look's photo-based place guess is the fallback.
- **Privacy:** the position is kept in memory only, for six hours, never written to the database, and never shown to anyone else.
- There is no hand-written list of sights: a new place needs nothing from us.

### Directions: the blue arrow

Ask can guide the person to something nearby: "where's the nearest subway?", "take me to a Tim Hortons", "any landmarks around here?".

- **Asking:** Only a question opened by the directions gesture carries the two tools, since declaring them makes every other answer start later. Gemini calls `find_place` with a category (restaurant, cafe, subway, landmark, park, washroom and so on) and, when one was named, a place or brand. The headset passes the call to `POST /navigate`, which searches OpenStreetMap (Overpass by category, Nominatim by name) within about 1.5 km, then 5 km, of the phone's latest fix, keeps the nearest match as the target and returns it with up to two others. EDITH says what it found, how far it is and that the arrow shows the way. "Stop directions" calls `stop_navigation`. Two finger guns pointing ahead open that question with "Where's our next adventure?" on screen, and two pointing up stop the directions at once and say so.
- **The arrow:** a blue arrow floats low in front of the person with the place's name, distance and walking minutes under it. The headset polls `GET /navigate` every second for the distance and compass bearing from the newest fix.
- **Finding north:** the headset has no compass, so the phone page sends its compass heading, and its GPS course while walking. Each reading, paired with the head's yaw at that moment, is one guess at where north lies in the headset's world; the guesses are averaged. Until north is known the arrow stays hidden and the label asks the person to hold the phone flat, pointing where they face. Recentring resets it.
- **Phone cadence:** while a target is set, the phone sends its position and heading every 2 s instead of 10 s.
- **Arrival:** within 20 m (more when the fix is poor, at most 50 m) the label says "You're at …", then the directions stop. A target is forgotten after two hours.
- **Privacy:** as for the position: coordinates leave the backend rounded to about 11 m and are never logged; the target lives in memory only.

### Under consideration

- **Scroll mode:** a toggle where EDITH tags interesting things around you by itself; the always-on version of Scan.
- **Spots:** a small globe beside the Scrapbook with a pin per confirmed place.

## Hands-only controls

| Action | Gesture |
| --- | --- |
| Look | Pinch empty space with either hand, drag at least about 5 cm, let go; a shorter pinch is a tap |
| Collect | Fist of either hand on the snapshot, pull toward the chest; open the hand first to let it float back. A sound that grows brighter and longer with the rarity plays as it goes in |
| Ask | Thumb tap on the side of the index finger of either hand; stops on silence |
| Page or dismiss cards | Thumb swipe left or right with either hand |
| Start directions | Both hands as finger guns pointing straight ahead, held about 0.3 s; EDITH asks "Where's our next adventure?" and listens for the destination |
| Stop directions | Both hands as finger guns pointing up, held about 0.3 s |
| Scan | Both palms open and facing away, held about 0.5 s; pinch a pin to open it |
| App menu | Turn the left wrist inward and poke with the right index |
| Scrapbook | Hold both palms up side by side for half a second to open, and again to close; thumb swipe or pinch-drag to scroll; pinch a chip or a side card |
| Spots globe | One-hand grab to spin; two-hand pull to zoom |

**Rules**

- Never use a pinch with the palm facing you: that is Quest's system menu gesture.
- Every gesture works with either hand. One hand pinches at a time: the hand that pinches first owns the pinch until its tap or its box is done, and a pinch the other hand makes meanwhile is ignored until that hand lets go. The right hand wins a tie.
- Each hand can carry its own card, so one hand can hold a card while the other pinches. A card one hand holds cannot be grabbed by the other.
- Keep gestures in front of the chest and under a second long.
- A grab only counts when the hand was clearly open within the last second before it closes into a fist, so a hand relaxing after a pinch never grabs.
- Every gesture gets visual or audio feedback.
- It should also be made easy at the end for people to start a new profile (new interests, new voices, new wtv yk)

## Architecture

- **Headset:** Unity and C# with the Meta XR SDK v74 or later; camera frames from the Passthrough Camera API. The app stays thin.
- **Backend:** Python with FastAPI, hosted on Railway. Every AI call, the search and the memory sit behind it, so API keys never touch the device. Run a single instance: the phone's location is held in memory (see below).
- **Database:** Tiger Data (Tiger Cloud, managed Postgres) with pgvector for embeddings, with pgvectorscale available if search needs it. This is also our Tiger Data sponsor track entry, so the database should be visible in the demo (for example, Scrapbook search and déjà vu both run on it).
- **Agents:** four, one endpoint each, with no general orchestration layer: Welcome (first launch), Card (Look), Query (Ask), Scan (Scan).
- **Models:** Gemini API for everything, one key and one SDK. Model IDs live in backend config, so any row below can change with no headset rebuild. OpenAI stays as an optional fallback for Card and Scan only, and is not part of the first build.
- **Targets:** Card results stream, with first words within 1.5 s of letting go of the pinch. Hold the frame rate with no dropped frames; read the camera only on demand; keep JPEG encoding off the main thread; no debug recording in release builds.

### Model per feature

| Feature | Agent | Model | Why |
| --- | --- | --- | --- |
| Look: name, fun fact, rarity, place guess | Card | Gemini 3.5 Flash-Lite (`gemini-3.5-flash-lite`), streamed | Fastest option, needed for the 1.5 s first-words target. Step up to `gemini-3.8-flash` if rarity or fact quality is weak. |
| Scan: pick 2 or 3 things with point, box and hook | Scan | Gemini 3.5 Flash-Lite (`gemini-3.5-flash-lite`), structured JSON output, with OpenAI `gpt-4o` asked as well after 2 s | One image-in call; Flash missed the 7 s scan budget on the headset. The first good answer of the two wins. |
| Ask: spoken answer | Query | Gemini 3.8 Live, the default low-latency variant | Native speech in and out, silence detection, and the voice choices for EDITH's voice; confirm the exact ID in the Live API docs. |
| Ask: web answers | Query | Gemini with Google Search grounding | Answers current questions with no separate search service. |
| Welcome: intro, interests, practice Look | Welcome | Gemini 3.8 Live, with the Card model for the practice Look | Same voice session as Ask, so the voice picked on welcome carries over. |
| Fun-fact kind matching, déjà vu, Scrapbook voice search | none | `gemini-embedding-2`, stored in pgvector | Multimodal, so a cropped photo and its label share one space; déjà vu can match on the image itself. Fix the vector size in config before the first row is written. |

Voice search in the Scrapbook transcribes through the same Live session; there is no separate speech-to-text service.

## What needs building

Order: Look and Collect with rarity, then Ask, then the Scan, then the phone's location, then the Scrapbook and Spots. An item is done only when it is finished end to end, UI included.

**Headset (Unity)**

- [ ] Passthrough camera permission and frame capture
- [ ] Pinch-and-drag frame and crop
- [ ] Label, Collect by grab and pocket, memory card with rarity reveal
- [ ] Thumb-tap Ask, answer orb that perches on the snapshot asked about, swipe to dismiss
- [ ] Scan: palms-out pose, one front frame, pins with hooks placed by depth, pinch a pin to open its card, Ask aimed at the nearest pin or card
- [ ] Wrist menu
- [ ] Welcome: intro, interests, practice Look
- [ ] Performance budget

**Backend and AI**

- [ ] FastAPI backend on Railway and Tiger Data Postgres with pgvector; API keys server-side only
- [ ] Agent endpoints (Welcome, Card, Query, Scan) with streamed Card results and the 1.5 s first-words target
- [ ] Vision call: label, card text, rarity tier, place guess
- [ ] Scan: pick the 2 or 3 things in one frame that make the most fun dares, with a point, a box and a dare each; no repeated dare per object in a session
- [ ] EDITH's voice
- [ ] Rarity tier and place guess from the card call; déjà vu matching from embeddings
- [ ] Web-search answers for Ask
- [ ] Phone location: place context for Look, Scan and Ask; named sights nearby for the Scan
- [ ] Place confirm and geocoding (only if Spots ships)
- [x] Directions: find_place / stop_navigation tools, `/navigate`, phone compass and course, blue arrow with north calibration

**Scrapbook and visuals**

- [x] Scrapbook deck: rarity frames, rarity chips, open-book gesture
- [ ] Scrapbook: voice search
- [ ] Spots globe with pins and labelled seeded sample data (only if Spots ships)
- [ ] A sound per rarity tier on collect
- [ ] UI design pass across the headset app. Target feel: liquid glass.

## Open decisions

- [ ] **Gemini only or Gemini plus OpenAI fallback:** start Gemini only; add OpenAI for Card and Scan only if Gemini quality or uptime is a problem.
- [ ] **Scroll mode and Spots:** in or out.
- [x] **Challenge mode for Scan:** no separate mode. Every pin's hook is a playful dare tied to that thing, doable right there in under a minute (decided Nov 21).
- [ ] **Name:** EDITH for now; Magpie, Goose and Nosy are the backups.
- [ ] **Sponsor API:** Tiger Data is the track we are going for; decide whether a second sponsor prize is worth chasing, and use each one somewhere visible.
- [ ] **Team roles:** who owns the headset, backend and AI, and the Scrapbook.
- [ ] **Unlocked badge:** a badge the first time a kind of thing enters your collection.
- [ ] **Ask as a passive companion or a capture tool.**
