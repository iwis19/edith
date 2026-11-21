import asyncio
import uuid

import pytest
from pydantic import SecretStr

from edith_backend.app import create_app
from edith_backend.events import Name

from .conftest import PROFILE, client_for, look_files, make_jpeg, meta_json, ndjson

HEADERS = {"X-Edith-Profile": PROFILE}


async def test_health_reports_the_database_state(client):
    response = await client.get("/health")
    assert response.status_code == 200
    assert response.json()["db"] == "disabled"


async def test_a_look_streams_the_card_then_done_and_records_it(client, store):
    response = await client.post("/look", files=look_files(), headers=HEADERS)
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/x-ndjson"
    events = ndjson(response)
    assert [e["type"] for e in events] == ["accepted", "name", "fact", "rarity", "done"]
    assert events[0]["look_id"] == 7
    times = [e["t_ms"] for e in events]
    assert times == sorted(times)

    await asyncio.sleep(0)
    [record] = store.records
    assert record.look_ref.hex == events[0]["look_ref"]
    assert record.profile_id == uuid.UUID(PROFILE)
    assert (record.status, record.name, record.fact, record.rarity) == (
        "done", events[1]["name"], events[2]["text"], events[3]["tier"])
    assert record.fact_sources == events[2]["sources"]


async def test_with_a_device_token_only_requests_carrying_it_are_served(settings, store):
    settings.device_token = SecretStr("s3cret")
    async with client_for(create_app(settings, store=store)) as c:
        for token in (None, "wrong"):
            headers = HEADERS | ({"X-Edith-Token": token} if token else {})
            response = await c.post("/look", files=look_files(), headers=headers)
            assert response.status_code == 401
            assert response.json()["error"]["code"] == "bad_token"
        ok = await c.post("/look", files=look_files(), headers=HEADERS | {"X-Edith-Token": "s3cret"})
        assert ok.status_code == 200
        assert (await c.get("/health")).status_code == 200


def test_a_public_backend_refuses_to_start_without_a_device_token(settings):
    settings.public_server = True
    with pytest.raises(RuntimeError, match="DEVICE_TOKEN"):
        create_app(settings)
    settings.device_token = SecretStr("t")
    create_app(settings)


@pytest.mark.parametrize("headers", [{}, {"X-Edith-Profile": "not-a-uuid"}])
async def test_the_profile_header_must_be_a_uuid(client, headers):
    response = await client.post("/look", files=look_files(), headers=headers)
    assert response.status_code == 400
    assert response.json()["error"]["code"] == "bad_profile"


@pytest.mark.parametrize(
    ("files", "status", "code"),
    [
        ({"image": ("crop.jpg", make_jpeg(), "image/jpeg")}, 422, "invalid_meta"),
        (look_files(meta="{not json"), 422, "invalid_meta"),
        (look_files(meta=meta_json(source="lasso")), 422, "invalid_meta"),
        (look_files(meta=meta_json(width=0)), 422, "invalid_meta"),
        ({"meta": (None, meta_json())}, 422, "missing_image"),
        (look_files(image=b"\x89PNG\r\n\x1a\n"), 422, "invalid_image"),
        (look_files(image=b"\xff\xd8\xff\xd9"), 422, "invalid_image"),
        (look_files(image=make_jpeg(1025, 300)), 422, "image_dimensions"),
        (look_files(image=make_jpeg() + b"\x00" * (2 * 1024 * 1024)), 413, "image_too_large"),
    ],
)
async def test_bad_requests_are_refused_before_streaming(client, store, files, status, code):
    response = await client.post("/look", files=files, headers=HEADERS)
    assert response.status_code == status
    assert response.json()["error"]["code"] == code
    assert store.records == []


class FailingAgent:
    async def run(self, image, meta, profile_id):
        yield Name(name="a mug", confidence=0.9)
        raise RuntimeError("model fell over")


class StuckAgent:
    async def run(self, image, meta, profile_id):
        yield Name(name="a mug", confidence=0.9)
        await asyncio.sleep(10)


@pytest.mark.parametrize(("agent", "code"), [(FailingAgent(), "agent_failed"), (StuckAgent(), "timeout")])
async def test_an_agent_failure_ends_the_stream_with_an_error_and_is_recorded(settings, store, agent, code):
    settings.look_timeout_s = 0.2
    async with client_for(create_app(settings, agent=agent, store=store)) as c:
        events = ndjson(await c.post("/look", files=look_files(), headers=HEADERS))
    assert [e["type"] for e in events] == ["accepted", "name", "error"]
    assert events[-1]["code"] == code
    await asyncio.sleep(0)
    [record] = store.records
    assert (record.status, record.name, record.fact) == ("error", "a mug", None)
