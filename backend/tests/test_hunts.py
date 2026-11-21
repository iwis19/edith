import asyncio
import json
import uuid
from datetime import UTC, datetime, timedelta

import pytest

from edith_backend.agents.ask import LiveAsk
from edith_backend.agents.scan import ScanAgent
from edith_backend.app import create_app
from edith_backend.db import DisabledStore
from edith_backend.events import Fact, LookDetails, Name, RarityEvent, ScanPick
from edith_backend.hunts import Hunts, Target

from .conftest import PROFILE, client_for, look_files, ndjson
from .test_ask import google, setup_of
from .test_scan import FakeModel, FakeStore, post_scan

HEADERS = {"X-Edith-Profile": PROFILE}
OTHER = {"X-Edith-Profile": str(uuid.UUID("6f1c2a4e-0000-4000-8000-000000000002"))}
START = datetime(2026, 11, 21, 10, 0, tzinfo=UTC)

TARGETS = [
    Target(0, "something red"), Target(1, "something round"), Target(2, "something wooden"),
    Target(3, "something Uncommon or better", "uncommon"), Target(4, "something Rare or better", "rare"),
]


class Clock:
    def __init__(self) -> None:
        self.now = START

    def __call__(self) -> datetime:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += timedelta(seconds=seconds)


async def draft(place, interests):
    draft.calls.append((place, list(interests)))
    return list(TARGETS)


draft.calls = []


class ScriptedAgent:
    """Each Look gets the next script: what the card agent reports, in the order the real one reports it."""

    def __init__(self, *scripts) -> None:
        self.scripts, self.targets = list(scripts), []

    async def run(self, image, meta, profile_id, targets=None):
        self.targets.append(targets)
        name, confidence, tier, pick = self.scripts.pop(0)
        yield Name(name=name, confidence=confidence)
        if tier:
            yield RarityEvent(tier=tier)
        yield LookDetails(kind="thing", hunt_pick=pick)
        yield Fact(text="A fact.", sources=[])


def make(settings, store, *scripts, **kwargs):
    clock = Clock()
    hunts = Hunts(store, draft, duration_s=settings.hunt_seconds, clock=clock, **kwargs)
    agent = ScriptedAgent(*scripts)
    return create_app(settings, agent=agent, store=store, hunts=hunts), agent, clock, hunts


async def look(client):
    return ndjson(await client.post("/look", files=look_files(), headers=HEADERS))


def of_type(events, kind):
    return [e for e in events if e["type"] == kind]


async def test_a_hunt_starts_reads_back_and_stops(settings, store):
    draft.calls.clear()
    app, _, clock, _ = make(settings, store)
    async with client_for(app) as c:
        assert (await c.get("/hunt", headers=HEADERS)).json()["error"]["code"] == "no_hunt"
        started = await c.post("/hunt", json={"interests": ["bikes", " "]}, headers=HEADERS)
        assert started.status_code == 200
        body = started.json()
        assert set(body) == {"hunt_ref", "status", "started_at", "ends_at", "ended_at", "remaining_s", "place",
                             "found", "targets"}
        assert len(body["hunt_ref"]) == 32
        assert (body["status"], body["remaining_s"], body["found"], body["ended_at"]) == ("active", 300, 0, None)
        assert body["started_at"] == "2026-11-21T10:00:00Z" and body["ends_at"] == "2026-11-21T10:05:00Z"
        assert body["targets"][3] == {"index": 3, "text": "something Uncommon or better", "tier": "uncommon",
                                      "found": False, "found_at": None, "look_ref": None, "memory_id": None}
        assert draft.calls == [(None, ["bikes"])]

        clock.advance(100)
        current = (await c.get("/hunt", headers=HEADERS)).json()
        assert (current["hunt_ref"], current["remaining_s"]) == (body["hunt_ref"], 200)
        assert (await c.get("/hunt", headers=OTHER)).status_code == 404

        stopped = (await c.post("/hunt/stop", headers=HEADERS)).json()
        assert (stopped["status"], stopped["remaining_s"]) == ("stopped", 0)
        assert stopped["ended_at"] == "2026-11-21T10:01:40Z"
        assert (await c.get("/hunt", headers=HEADERS)).status_code == 404
        again = await c.post("/hunt/stop", headers=HEADERS)
        assert (again.status_code, again.json()["error"]["code"]) == (404, "no_hunt")


async def test_a_hunt_times_out_with_the_clock_and_needs_no_writer(settings, store):
    app, _, clock, hunts = make(settings, store)
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        clock.advance(299)
        assert (await c.get("/hunt", headers=HEADERS)).json()["remaining_s"] == 1
        clock.advance(2)
        assert (await c.get("/hunt", headers=HEADERS)).status_code == 404
        assert (await c.post("/hunt/stop", headers=HEADERS)).status_code == 404
        [saved] = (await c.get("/hunts", headers=HEADERS)).json()["hunts"]
        assert (saved["status"], saved["remaining_s"], saved["ended_at"]) == ("timeout", 0, None)


async def test_starting_a_hunt_stops_the_one_running(settings, store):
    app, _, clock, _ = make(settings, store)
    async with client_for(app) as c:
        first = (await c.post("/hunt", headers=HEADERS)).json()
        clock.advance(10)
        second = (await c.post("/hunt", headers=HEADERS)).json()
        assert first["hunt_ref"] != second["hunt_ref"]
        assert (await c.get("/hunt", headers=HEADERS)).json()["hunt_ref"] == second["hunt_ref"]
        listed = (await c.get("/hunts", headers=HEADERS)).json()["hunts"]
        assert [(h["hunt_ref"], h["status"]) for h in listed] == [
            (second["hunt_ref"], "active"), (first["hunt_ref"], "stopped")]


async def test_a_look_during_a_hunt_gets_the_open_targets_and_a_hunt_event_before_the_fact(settings, store):
    app, agent, _, _ = make(settings, store, ("a red mug", 0.9, "common", 0))
    async with client_for(app) as c:
        started = (await c.post("/hunt", headers=HEADERS)).json()
        events = await look(c)
        assert [e["type"] for e in events] == ["accepted", "name", "rarity", "hunt", "fact", "done"]
        assert agent.targets == [[(0, "something red"), (1, "something round"), (2, "something wooden"),
                                  (3, "something Uncommon or better"), (4, "something Rare or better")]]
        hunt = events[3]
        assert {k: v for k, v in hunt.items() if k != "t_ms"} == {
            "type": "hunt", "index": 0, "text": "something red", "found": 1, "total": 5, "status": "active"}
        state = (await c.get("/hunt", headers=HEADERS)).json()
        assert state["found"] == 1 and state["hunt_ref"] == started["hunt_ref"]
        assert state["targets"][0]["found"] is True
        assert state["targets"][0]["look_ref"] == events[0]["look_ref"]
        assert state["targets"][0]["found_at"] == "2026-11-21T10:00:00Z"


async def test_without_a_hunt_a_look_carries_no_targets_and_no_hunt_event(settings, store):
    app, agent, _, _ = make(settings, store, ("a red mug", 0.9, "rare", 0))
    async with client_for(app) as c:
        events = await look(c)
    assert agent.targets == [None]
    assert [e["type"] for e in events] == ["accepted", "name", "rarity", "fact", "done"]


@pytest.mark.parametrize(("name", "confidence"), [("Maybe a red mug", 0.9), ("a red mug", 0.49)])
async def test_an_unsure_look_ticks_nothing(settings, store, name, confidence):
    app, _, _, _ = make(settings, store, (name, confidence, "rare", 0))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        events = await look(c)
        assert of_type(events, "hunt") == []
        assert (await c.get("/hunt", headers=HEADERS)).json()["found"] == 0


async def test_a_rarity_target_is_ticked_by_a_look_of_that_tier_or_better_lowest_first(settings, store):
    app, _, _, _ = make(settings, store, ("a rolex", 0.9, "epic", None), ("a mug", 0.9, "uncommon", None),
                        ("a bench", 0.9, "common", None))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        first, second, third = [of_type(await look(c), "hunt") for _ in range(3)]
        assert [h["index"] for h in first] == [3]
        assert [h["index"] for h in second] == []
        assert third == []
        state = (await c.get("/hunt", headers=HEADERS)).json()
        assert [t["found"] for t in state["targets"]] == [False, False, False, True, False]


async def test_a_look_that_meets_both_tiers_ticks_only_one_target(settings, store):
    app, _, _, _ = make(settings, store, ("a rolex", 0.9, "rare", 0))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        [hunt] = of_type(await look(c), "hunt")
        assert (hunt["index"], hunt["found"]) == (0, 1)


async def test_a_theme_pick_must_be_an_open_theme_target(settings, store):
    app, _, _, _ = make(settings, store, ("a ball", 0.9, "common", 1), ("a ball", 0.9, "common", 1),
                        ("a coin", 0.9, "common", 4), ("a coin", 0.9, "common", 9))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        assert [h["index"] for h in of_type(await look(c), "hunt")] == [1]
        assert of_type(await look(c), "hunt") == []
        assert of_type(await look(c), "hunt") == []
        assert of_type(await look(c), "hunt") == []


async def test_finding_all_five_ends_the_hunt(settings, store):
    app, _, _, _ = make(settings, store, *[("a thing", 0.9, "rare", i) for i in range(3)],
                        ("a ring", 0.9, "uncommon", None), ("a gem", 0.9, "rare", None))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        hunts = [of_type(await look(c), "hunt")[0] for _ in range(5)]
        assert [(h["found"], h["status"]) for h in hunts] == [
            (1, "active"), (2, "active"), (3, "active"), (4, "active"), (5, "found_all")]
        assert (await c.get("/hunt", headers=HEADERS)).status_code == 404
        [saved] = (await c.get("/hunts", headers=HEADERS)).json()["hunts"]
        assert (saved["status"], saved["found"], saved["remaining_s"]) == ("found_all", 5, 0)
        assert saved["ended_at"] is not None


async def test_a_look_after_the_timeout_ticks_nothing(settings, store):
    app, agent, clock, _ = make(settings, store, ("a red mug", 0.9, "common", 0))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        clock.advance(301)
        events = await look(c)
    assert agent.targets == [None]
    assert of_type(events, "hunt") == []


async def test_a_collected_found_card_is_linked_in_the_hunt_list(settings, store):
    app, _, _, _ = make(settings, store, ("a red mug", 0.9, "common", 0), ("a red cup", 0.9, "common", 1))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        refs = [(await look(c))[0]["look_ref"] for _ in range(2)]
        await asyncio.sleep(0)
        collected = await c.post("/collect", headers=HEADERS, files={
            "image": ("crop.jpg", look_files()["image"][1], "image/jpeg"),
            "meta": (None, json.dumps({"look_ref": refs[0]}))})
        memory_id = collected.json()["memory_id"]
        [saved] = (await c.get("/hunts", headers=HEADERS)).json()["hunts"]
        assert [(t["look_ref"], t["memory_id"]) for t in saved["targets"][:3]] == [
            (refs[0], memory_id), (refs[1], None), (None, None)]


async def test_hunts_are_per_profile_and_listed_with_a_limit(settings, store):
    app, _, _, _ = make(settings, store)
    async with client_for(app) as c:
        for _ in range(3):
            await c.post("/hunt", headers=HEADERS)
        await c.post("/hunt", headers=OTHER)
        assert len((await c.get("/hunts", headers=HEADERS)).json()["hunts"]) == 3
        assert len((await c.get("/hunts?limit=2", headers=HEADERS)).json()["hunts"]) == 2
        assert len((await c.get("/hunts", headers=OTHER)).json()["hunts"]) == 1
        assert (await c.get("/hunts?limit=0", headers=HEADERS)).status_code == 422


async def test_without_a_database_hunts_run_in_memory_and_the_list_is_503(settings):
    clock = Clock()
    hunts = Hunts(DisabledStore(), draft, clock=clock)
    agent = ScriptedAgent(("a red mug", 0.9, "common", 0))
    async with client_for(create_app(settings, agent=agent, store=DisabledStore(), hunts=hunts)) as c:
        assert (await c.post("/hunt", headers=HEADERS)).status_code == 200
        assert [e["type"] for e in await look(c)] == ["accepted", "name", "rarity", "hunt", "fact", "done"]
        assert (await c.get("/hunt", headers=HEADERS)).json()["found"] == 1
        listed = await c.get("/hunts", headers=HEADERS)
        assert (listed.status_code, listed.json()["error"]["code"]) == (503, "db_unavailable")


async def test_a_hunt_survives_a_restart_while_it_has_time_left(settings, store):
    app, _, clock, _ = make(settings, store, ("a red mug", 0.9, "common", 0))
    async with client_for(app) as c:
        await c.post("/hunt", headers=HEADERS)
        await look(c)
    clock.advance(60)
    reborn = Hunts(store, draft, clock=clock)
    app = create_app(settings, store=store, hunts=reborn)
    async with app.router.lifespan_context(app), client_for(app) as c:
        await asyncio.sleep(0.05)
        state = (await c.get("/hunt", headers=HEADERS)).json()
    assert (state["found"], state["remaining_s"]) == (1, 240)
    clock.advance(300)
    expired = Hunts(store, draft, clock=clock)
    await expired.restore()
    assert expired.current(uuid.UUID(PROFILE)) is None


async def test_a_scan_during_a_hunt_gets_the_open_targets_and_pick_numbers_are_repaired(settings, store):
    pick = lambda name, target: ScanPick(name=name, hook=f"Hook {name}.", point=(0.5, 0.5), box=(0.4, 0.4, 0.6, 0.6),
                                         target=target)
    model = FakeModel([pick("a ball", 1)], [pick("a globe", 1)])
    clock = Clock()
    hunts = Hunts(store, draft, clock=clock)
    scanner = ScanAgent(model, FakeStore())
    async with client_for(create_app(settings, store=store, scanner=scanner, hunts=hunts)) as c:
        before = await post_scan(c)
        assert before.json()["picks"][0]["target"] is None
        await c.post("/hunt", headers=HEADERS)
        during = await post_scan(c)
    assert during.json()["picks"][0]["target"] == 1
    assert model.targets == [set(), {0, 1, 2, 3, 4}]
    assert "scavenger hunt" not in model.systems[0] and "scavenger hunt" in model.systems[1]
    assert model.contexts[0] is None and "2. something round" in model.contexts[1]


async def test_ask_offers_the_hunt_tools_and_tells_the_open_targets_during_a_hunt(settings, store):
    ask = LiveAsk(google([]), "g-key", "gemini-3.8-live", "Kore", False)
    clock = Clock()
    hunts = Hunts(store, draft, clock=clock)
    async with client_for(create_app(settings, store=store, ask=ask, hunts=hunts)) as c:
        quiet = setup_of(await c.post("/ask/session", headers=HEADERS))
        declared = {f["name"] for t in quiet["tools"] for f in t.get("functionDeclarations", [])}
        assert {"start_hunt", "stop_hunt"} <= declared
        start = next(f for t in quiet["tools"] for f in t["functionDeclarations"] if f["name"] == "start_hunt")
        assert start["parameters"]["properties"]["interests"]["type"] == "ARRAY"
        assert "Targets still open" not in quiet["systemInstruction"]["parts"][0]["text"]

        await c.post("/hunt", headers=HEADERS)
        clock.advance(75)
        text = setup_of(await c.post("/ask/session", headers=HEADERS))["systemInstruction"]["parts"][0]["text"]
    assert "3 minutes 45 seconds left" in text
    assert "something red; something round" in text
    assert "Found so far: none yet" in text
