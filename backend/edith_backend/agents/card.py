import asyncio
import time
from collections.abc import AsyncIterator
from typing import Protocol
from uuid import UUID

from ..config import Settings
from ..events import CardEvent, Fact, LookMeta, Name, RarityEvent, Source


class CardAgent(Protocol):
    def run(self, image: bytes, meta: LookMeta, profile_id: UUID) -> AsyncIterator[CardEvent]:
        """Yield name, fact and rarity. Raise to fail the Look."""
        ...


_CANNED = [
    ("ceramic pour-over coffee dripper", 0.82, "common",
     "The spiral ridges inside a pour-over cone let air escape so water drains evenly through the grounds.",
     Source(title="Pour-over brewing", url="https://en.wikipedia.org/wiki/Pour-over_coffee")),
    ("mechanical keyboard", 0.91, "uncommon",
     "Many mechanical key switches are rated for over fifty million presses each.",
     Source(title="Keyboard technology", url="https://en.wikipedia.org/wiki/Keyboard_technology")),
    ("Swiss cheese plant", 0.88, "rare",
     "The holes in monstera leaves may help the plant catch sunflecks on a dim forest floor.",
     Source(title="Monstera deliciosa", url="https://en.wikipedia.org/wiki/Monstera_deliciosa")),
]


class StubCardAgent:
    """Streams canned cards on the real event schedule, so the headset pipe works before the model is wired in."""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._count = 0

    async def run(self, image: bytes, meta: LookMeta, profile_id: UUID) -> AsyncIterator[CardEvent]:
        name, confidence, tier, fact, source = _CANNED[self._count % len(_CANNED)]
        self._count += 1
        start = time.monotonic()

        async def at(ms: int) -> None:
            remaining = ms / 1000 - (time.monotonic() - start)
            if remaining > 0:
                await asyncio.sleep(remaining)

        await at(self._settings.stub_name_ms)
        yield Name(name=name, confidence=confidence)
        await at(self._settings.stub_fact_ms)
        yield Fact(text=fact, sources=[source])
        await at(self._settings.stub_rarity_ms)
        yield RarityEvent(tier=tier)
