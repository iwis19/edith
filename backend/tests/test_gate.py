import asyncio
import json
import time
import uuid
from datetime import UTC, datetime

from fastapi.testclient import TestClient
from pydantic import SecretStr

from edith_backend.app import _stream, create_app
from edith_backend.events import Fact, LookMeta, Name

from .conftest import PROFILE, RecordingStore, look_files, make_jpeg


async def call(app, headers: list[tuple[bytes, bytes]], chunks: list[bytes]) -> tuple[int, dict, int]:
    """Drives the ASGI app directly. Returns the status, the JSON body and how many body chunks the app read."""
    pending = list(chunks)
    reads = 0
    sent: list[dict] = []

    async def receive():
        nonlocal reads
        reads += 1
        body = pending.pop(0) if pending else b""
        return {"type": "http.request", "body": body, "more_body": bool(pending)}

    async def send(message):
        sent.append(message)

    scope = {"type": "http", "asgi": {"version": "3.0"}, "http_version": "1.1", "method": "POST", "scheme": "http",
             "path": "/look", "raw_path": b"/look", "query_string": b"", "root_path": "",
             "headers": headers, "client": ("127.0.0.1", 1), "server": ("test", 80)}
    await app(scope, receive, send)
    start = next(m for m in sent if m["type"] == "http.response.start")
    body = b"".join(m.get("body", b"") for m in sent if m["type"] == "http.response.body")
    return start["status"], json.loads(body), reads


async def test_a_wrong_token_is_refused_before_any_of_the_body_is_read(settings, store):
    settings.device_token = SecretStr("s3cret")
    app = create_app(settings, store=store)
    headers = [(b"x-edith-token", b"nope"), (b"content-length", b"999999999"),
               (b"content-type", b"multipart/form-data; boundary=x")]
    status, body, reads = await call(app, headers, [b"x" * 1024])
    assert (status, body["error"]["code"], reads) == (401, "bad_token", 0)


async def test_a_body_with_no_length_is_cut_off_at_the_cap(settings, store):
    app = create_app(settings, store=store)
    headers = [(b"x-edith-profile", PROFILE.encode()), (b"content-type", b"multipart/form-data; boundary=x")]
    part = b'--x\r\nContent-Disposition: form-data; name="image"; filename="a.jpg"\r\nContent-Type: image/jpeg\r\n\r\n'
    chunk = b"y" * (512 * 1024)
    status, body, reads = await call(app, headers, [part] + [chunk] * 20)
    assert (status, body.get("error", {}).get("code")) == (413, "body_too_large"), body
    assert reads < 20
    assert store.records == []


class QuickAgent:
    """Each event takes 10 ms of real waiting, so the deadline is armed while the agent is awaited."""

    async def run(self, image, meta, profile_id):
        await asyncio.sleep(0.01)
        yield Name(name="a mug", confidence=0.9)
        await asyncio.sleep(0.01)
        yield Fact(text="Handles came late to European cups.", sources=[])


async def test_the_look_deadline_does_not_count_time_the_client_spends_reading(settings, store):
    settings.look_timeout_s = 0.1
    meta = LookMeta(look_id=1, source="pinch", captured_at=datetime.now(UTC))
    stream = _stream(QuickAgent(), store, settings, uuid.uuid4(), meta, make_jpeg(), (640, 480), time.monotonic())
    types = []
    async for line in stream:
        types.append(json.loads(line)["type"])
        await asyncio.sleep(0.15)
    assert types == ["accepted", "name", "fact", "done"]


class SlowStore(RecordingStore):
    async def record_look(self, record):
        await asyncio.sleep(0.3)
        await super().record_look(record)


def test_shutdown_waits_for_looks_still_being_written(settings):
    store = SlowStore()
    with TestClient(create_app(settings, store=store)) as client:
        response = client.post("/look", files=look_files(), headers={"X-Edith-Profile": PROFILE})
        assert response.status_code == 200
        assert store.records == []
    assert [r.status for r in store.records] == ["done"]
