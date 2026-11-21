"""Embeds the kind of object a Look found, so two names for one kind ("mug", "coffee cup") can be matched. Gemini over
its REST API with httpx, like the card writers."""
import asyncio
import logging
import math
from array import array
from collections import OrderedDict
from typing import Protocol

import httpx

from .writers import GEMINI_API

log = logging.getLogger("edith.look")


class KindEmbedder(Protocol):
    async def embed(self, kind: str) -> array | None:
        """The embedding of `kind`, or None when it could not be had in time. Never raises."""
        ...


def cosine(a: array, b: array) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    norms = math.sqrt(sum(x * x for x in a)) * math.sqrt(sum(y * y for y in b))
    return dot / norms if norms else 0.0


class GeminiEmbedder:
    """Gives each call `timeout_s` in all and remembers recent kinds, which repeat across Looks, so most Looks make no
    call. A failure is not remembered, so the next Look tries again."""

    def __init__(self, client: httpx.AsyncClient, key: str, model: str, dimensions: int, timeout_s: float = 0.3,
                 max_cached: int = 512) -> None:
        self._client, self._key, self._model, self._dimensions = client, key, model, dimensions
        self._timeout_s, self._max_cached = timeout_s, max_cached
        self._cache: OrderedDict[str, array] = OrderedDict()

    async def embed(self, kind: str) -> array | None:
        if (hit := self._cache.get(kind)) is not None:
            self._cache.move_to_end(kind)
            return hit
        try:
            async with asyncio.timeout(self._timeout_s):
                vector = await self._request(kind)
        except Exception as exc:
            log.warning("embedding a kind failed: %s", type(exc).__name__)
            return None
        self._cache[kind] = vector
        while len(self._cache) > self._max_cached:
            self._cache.popitem(last=False)
        return vector

    async def _request(self, kind: str) -> array:
        response = await self._client.post(
            f"{GEMINI_API}{self._model}:embedContent", headers={"x-goog-api-key": self._key},
            json={"content": {"parts": [{"text": kind}]}, "outputDimensionality": self._dimensions},
        )
        response.raise_for_status()
        values = response.json()["embedding"]["values"]
        if len(values) != self._dimensions:
            raise ValueError(f"the embedding has {len(values)} values, not {self._dimensions}")
        return array("f", values)
