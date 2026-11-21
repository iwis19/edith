import asyncio
import json
import sys
import uuid

import httpx
import pytest

from edith_backend.app import create_app
from edith_backend.config import Settings
from edith_backend.db import LookRecord, Memory

def pytest_asyncio_loop_factories(config, item):
    # psycopg's async mode cannot run on Windows' default proactor loop.
    return {"selector": asyncio.SelectorEventLoop} if sys.platform == "win32" else None


PROFILE = str(uuid.UUID("6f1c2a4e-0000-4000-8000-000000000001"))


def make_jpeg(width: int = 640, height: int = 480, sof: int = 0xC0) -> bytes:
    """A minimal JPEG byte stream: SOI, an APP0 segment, a frame header with the given size, EOI."""
    app0 = b"\xff\xe0" + (16).to_bytes(2, "big") + b"JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00"
    frame = bytes([0xFF, sof]) + (11).to_bytes(2, "big") + b"\x08" + height.to_bytes(2, "big") \
        + width.to_bytes(2, "big") + b"\x01\x01\x11\x00"
    return b"\xff\xd8" + app0 + frame + b"\xff\xd9"


def meta_json(**overrides) -> str:
    meta = {"look_id": 7, "source": "pinch", "captured_at": "2026-11-21T10:00:00Z"}
    meta.update(overrides)
    return json.dumps(meta)


class RecordingStore:
    def __init__(self) -> None:
        self.records: list[LookRecord] = []
        self.memories: dict[int, tuple[Memory, uuid.UUID, bytes, uuid.UUID]] = {}

    async def open(self) -> None:
        pass

    async def close(self) -> None:
        pass

    async def status(self) -> str:
        return "disabled"

    async def record_look(self, record: LookRecord) -> None:
        self.records.append(record)

    async def collect(self, profile_id, look_ref, image, collected_on):
        for memory, owner, _, ref in self.memories.values():
            if ref == look_ref and owner == profile_id:
                return memory.id
        record = next((r for r in self.records if r.look_ref == look_ref and r.profile_id == profile_id), None)
        if record is None or record.status != "done" or record.name is None:
            return None
        memory = Memory(
            id=len(self.memories) + 1, name=record.name, fact=record.fact, rarity=record.rarity, place=record.place,
            collected_on=collected_on,
        )
        self.memories[memory.id] = (memory, profile_id, image, look_ref)
        return memory.id

    async def list_memories(self, profile_id, limit):
        mine = [m for m, owner, _, _ in self.memories.values() if owner == profile_id]
        return sorted(mine, key=lambda m: (m.collected_on, m.id), reverse=True)[:limit]

    async def memory_image(self, profile_id, memory_id):
        entry = self.memories.get(memory_id)
        return entry[2] if entry and entry[1] == profile_id else None


@pytest.fixture
def settings() -> Settings:
    return Settings(_env_file=None, stub_name_ms=0, stub_fact_ms=0, stub_rarity_ms=0)


@pytest.fixture
def store() -> RecordingStore:
    return RecordingStore()


def client_for(app) -> httpx.AsyncClient:
    return httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test")


@pytest.fixture
async def client(settings, store):
    async with client_for(create_app(settings, store=store)) as c:
        yield c


def look_files(image: bytes | None = None, meta: str | None = None) -> dict:
    return {"image": ("crop.jpg", image if image is not None else make_jpeg(), "image/jpeg"),
            "meta": (None, meta if meta is not None else meta_json())}


def ndjson(response: httpx.Response) -> list[dict]:
    return [json.loads(line) for line in response.text.splitlines() if line]
