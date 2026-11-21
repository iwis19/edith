import asyncio
import logging
import time
from collections.abc import AsyncIterator, Callable
from typing import Protocol
from uuid import UUID

import httpx
from openai import AsyncOpenAI

from ..config import Settings
from ..db import Store
from ..events import CardEvent, Fact, LookMeta, Name, Rarity, RarityEvent, Source
from ..location import Whereabouts
from .card_model import CardModelAgent, FactHistory
from .embeddings import GeminiEmbedder, KindEmbedder
from .writers import GeminiWriter, OpenAIWriter

log = logging.getLogger("edith")


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


def make_embedder(settings: Settings) -> KindEmbedder | None:
    if not settings.gemini_api_key:
        return None
    return GeminiEmbedder(httpx.AsyncClient(), settings.gemini_api_key.get_secret_value(), settings.embedding_model,
                          settings.embedding_dimensions, settings.embedding_timeout_s)


def _make_card_agent(
    settings: Settings, whereabouts: Callable[[UUID], Whereabouts | None] = lambda _: None, store: Store | None = None
) -> CardAgent:
    """The writers come from card_provider. Without that provider's key the stub answers, so the pipe works with no
    keys at all. `whereabouts` gives the phone's place for a profile. `store` keeps the facts told across restarts, and
    a Gemini key matches kinds of object by embedding."""
    history = dict(whereabouts=whereabouts, embedder=make_embedder(settings), archive=store,
                   facts=FactHistory(similarity=settings.kind_similarity))
    if settings.card_provider == "openai" and settings.openai_api_key:
        client = AsyncOpenAI(api_key=settings.openai_api_key.get_secret_value(),
                             timeout=settings.look_timeout_s, max_retries=1)
        return CardModelAgent(
            name_writer=OpenAIWriter(client, settings.openai_name_model),
            fact_writer=OpenAIWriter(client, settings.openai_fact_model, settings.openai_fact_reasoning_effort),
            **history,
        )
    if settings.card_provider == "gemini" and settings.gemini_api_key:
        writer = GeminiWriter(httpx.AsyncClient(timeout=settings.look_timeout_s),
                              settings.gemini_api_key.get_secret_value(), settings.gemini_card_model,
                              settings.gemini_thinking_level)
        return CardModelAgent(name_writer=writer, fact_writer=writer, **history)
    log.warning("no %s key, so Looks get canned stub cards", settings.card_provider)
    return StubCardAgent(settings)


_TOUR: tuple[Rarity, ...] = ("common", "uncommon", "rare", "epic", "legendary", "mythic")


class RarityTour:
    """Demo only: gives each Look the next tier in turn, so every rarity sound and glow can be seen in one session."""

    def __init__(self, inner: CardAgent) -> None:
        self._inner = inner
        self._count = 0

    async def run(self, image: bytes, meta: LookMeta, profile_id: UUID) -> AsyncIterator[CardEvent]:
        tier = _TOUR[self._count % len(_TOUR)]
        self._count += 1
        async for event in self._inner.run(image, meta, profile_id):
            yield RarityEvent(tier=tier) if isinstance(event, RarityEvent) else event


def make_card_agent(
    settings: Settings, whereabouts: Callable[[UUID], Whereabouts | None] = lambda _: None, store: Store | None = None
) -> CardAgent:
    agent = _make_card_agent(settings, whereabouts, store)
    return RarityTour(agent) if settings.demo_rarity_tour else agent
