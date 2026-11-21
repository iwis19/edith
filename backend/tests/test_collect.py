import asyncio
import uuid

from edith_backend.app import create_app
from edith_backend.config import Settings
from edith_backend.db import DisabledStore
from edith_backend.events import LookDetails, Name, RarityEvent

from .conftest import PROFILE, client_for, look_files, make_jpeg, ndjson

HEADERS = {"X-Edith-Profile": PROFILE}
OTHER = {"X-Edith-Profile": str(uuid.UUID(int=2))}
CROP = make_jpeg(320, 240)


async def look(client, headers=HEADERS) -> tuple[str, list[dict]]:
    events = ndjson(await client.post("/look", files=look_files(), headers=headers))
    await asyncio.sleep(0)
    return events[0]["look_ref"], events


def collect(client, look_ref: str, image: bytes = CROP, headers=HEADERS):
    files = {"image": ("crop.jpg", image, "image/jpeg"), "meta": (None, f'{{"look_ref": "{look_ref}"}}')}
    return client.post("/collect", files=files, headers=headers)


async def test_collecting_a_finished_look_saves_a_memory_from_the_stored_card(client):
    ref, events = await look(client)
    response = await collect(client, ref)
    assert response.status_code == 200
    memory_id = response.json()["memory_id"]

    [memory] = (await client.get("/memories", headers=HEADERS)).json()["memories"]
    assert memory["id"] == memory_id
    assert memory["look_ref"] == ref
    assert (memory["name"], memory["fact"], memory["rarity"]) == (events[1]["name"], events[2]["text"], events[3]["tier"])
    assert memory["sources"] == events[2]["sources"]
    assert "image" not in memory


async def test_a_collect_during_the_stream_waits_for_the_look_to_be_recorded(store, monkeypatch):
    settings = Settings(_env_file=None, stub_name_ms=0, stub_fact_ms=300, stub_rarity_ms=350)
    ref = uuid.UUID(hex="ab" * 16)
    monkeypatch.setattr("edith_backend.app.uuid.uuid4", lambda: ref)
    async with client_for(create_app(settings, store=store)) as c:
        looking = asyncio.create_task(c.post("/look", files=look_files(), headers=HEADERS))
        await asyncio.sleep(0.1)
        assert not looking.done()
        collected = await collect(c, ref.hex)
        events = ndjson(await looking)
        [memory] = (await c.get("/memories", headers=HEADERS)).json()["memories"]
    assert collected.status_code == 200
    assert memory["fact"] == events[2]["text"]
    assert memory["rarity"] == events[3]["tier"]


async def test_collecting_twice_returns_the_same_memory(client):
    ref, _ = await look(client)
    first = await collect(client, ref)
    second = await collect(client, ref, image=make_jpeg(100, 100))
    assert second.json() == first.json()
    assert len((await client.get("/memories", headers=HEADERS)).json()["memories"]) == 1
    image = await client.get(f"/memories/{first.json()['memory_id']}/image", headers=HEADERS)
    assert image.content == CROP


async def test_an_unknown_look_ref_is_404(client):
    response = await collect(client, "0" * 32)
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "unknown_look"


async def test_another_profiles_look_cannot_be_collected(client):
    ref, _ = await look(client)
    response = await collect(client, ref, headers=OTHER)
    assert response.status_code == 404
    assert response.json()["error"]["code"] == "unknown_look"
    assert (await client.get("/memories", headers=OTHER)).json() == {"memories": []}


async def test_memories_are_listed_newest_first_for_the_calling_profile(client):
    refs = [(await look(client))[0] for _ in range(3)]
    for ref in refs:
        await collect(client, ref)
    listed = (await client.get("/memories", headers=HEADERS)).json()["memories"]
    assert [m["look_ref"] for m in listed] == refs[::-1]
    limited = (await client.get("/memories?limit=1", headers=HEADERS)).json()["memories"]
    assert [m["look_ref"] for m in limited] == [refs[2]]


async def test_the_image_route_serves_the_crop_as_a_jpeg_to_its_owner_only(client):
    ref, _ = await look(client)
    memory_id = (await collect(client, ref)).json()["memory_id"]
    ok = await client.get(f"/memories/{memory_id}/image", headers=HEADERS)
    assert ok.status_code == 200
    assert ok.headers["content-type"] == "image/jpeg"
    assert ok.content == CROP
    for headers, mid in [(OTHER, memory_id), (HEADERS, memory_id + 1)]:
        missing = await client.get(f"/memories/{mid}/image", headers=headers)
        assert missing.status_code == 404
        assert missing.json()["error"]["code"] == "unknown_memory"


async def test_a_collect_needs_a_jpeg_and_a_look_ref(client):
    ref, _ = await look(client)
    not_jpeg = await collect(client, ref, image=b"nope")
    assert not_jpeg.status_code == 422
    bad_ref = await collect(client, "not-a-ref")
    assert (bad_ref.status_code, bad_ref.json()["error"]["code"]) == (422, "invalid_meta")


async def test_without_a_database_the_memory_routes_answer_503(settings):
    async with client_for(create_app(settings, store=DisabledStore())) as c:
        responses = [await collect(c, "0" * 32), await c.get("/memories", headers=HEADERS),
                     await c.get("/memories/1/image", headers=HEADERS)]
    assert [(r.status_code, r.json()["error"]["code"]) for r in responses] == [(503, "db_unavailable")] * 3


class DetailedAgent:
    async def run(self, image, meta, profile_id):
        yield Name(name="a red mug", confidence=0.8)
        yield LookDetails(kind="mug", place="Waterloo, Ontario, Canada")
        yield RarityEvent(tier="common")


async def test_a_memory_copies_the_looks_kind_and_place(settings, store):
    async with client_for(create_app(settings, agent=DetailedAgent(), store=store)) as c:
        ref, _ = await look(c)
        await collect(c, ref)
        [memory] = (await c.get("/memories", headers=HEADERS)).json()["memories"]
    assert (memory["kind"], memory["place"]) == ("mug", "Waterloo, Ontario, Canada")
