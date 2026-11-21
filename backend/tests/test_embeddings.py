import asyncio
import json
from array import array

import httpx
import pytest

from edith_backend.agents.embeddings import GeminiEmbedder, cosine


def embedder(handler, dimensions: int = 3, timeout_s: float = 0.3, **kwargs) -> GeminiEmbedder:
    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    return GeminiEmbedder(client, "g-key", "gemini-embedding-2", dimensions, timeout_s, **kwargs)


def answering(values: list[float], seen: list[httpx.Request] | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        if seen is not None:
            seen.append(request)
        return httpx.Response(200, json={"embedding": {"values": values}})

    return handler


async def test_a_kind_is_sent_to_the_embedding_model_with_the_configured_size():
    seen: list[httpx.Request] = []
    vector = await embedder(answering([0.5, 0.25, 1.0], seen)).embed("mug")
    assert list(vector) == [0.5, 0.25, 1.0]
    [request] = seen
    assert request.url.path == "/v1beta/models/gemini-embedding-2:embedContent"
    assert request.headers["x-goog-api-key"] == "g-key"
    assert json.loads(request.content) == {"content": {"parts": [{"text": "mug"}]}, "outputDimensionality": 3}


async def test_a_kind_seen_before_is_answered_without_another_call():
    seen: list[httpx.Request] = []
    e = embedder(answering([1, 0, 0], seen))
    assert await e.embed("mug") == await e.embed("mug")
    assert len(seen) == 1


async def test_the_cache_forgets_the_least_recently_used_kind():
    seen: list[httpx.Request] = []
    e = embedder(answering([1, 0, 0], seen), max_cached=2)
    for kind in ("mug", "lamp", "mug", "chair", "mug", "lamp"):
        await e.embed(kind)
    assert len(seen) == 4, "mug and lamp were fetched, then chair evicted lamp, then lamp again"


@pytest.mark.parametrize("handler", [
    lambda request: httpx.Response(429, json={"error": {"message": "quota"}}),
    lambda request: httpx.Response(200, json={"unexpected": True}),
    lambda request: httpx.Response(200, json={"embedding": {"values": [1.0, 2.0]}}),
    lambda request: (_ for _ in ()).throw(httpx.ConnectError("down")),
])
async def test_a_failed_or_malformed_answer_gives_none_and_is_tried_again_next_time(handler):
    e = embedder(handler)
    assert await e.embed("mug") is None
    e._client = httpx.AsyncClient(transport=httpx.MockTransport(answering([1, 0, 0])))
    assert list(await e.embed("mug")) == [1, 0, 0]


async def test_a_slow_answer_gives_none_when_the_timeout_runs_out():
    async def slow(request: httpx.Request) -> httpx.Response:
        await asyncio.sleep(5)
        return httpx.Response(200, json={"embedding": {"values": [1, 0, 0]}})

    started = asyncio.get_running_loop().time()
    assert await embedder(slow, timeout_s=0.05).embed("mug") is None
    assert asyncio.get_running_loop().time() - started < 1


def test_cosine_ignores_length_and_scores_a_near_match_above_an_unrelated_kind():
    mug, cup, lamp = array("f", [1, 0]), array("f", [4.8, 1.4]), array("f", [0.2, 1])
    assert cosine(mug, array("f", [3, 0])) == pytest.approx(1.0)
    assert cosine(mug, cup) > 0.95 > 0.3 > cosine(mug, lamp)
    assert cosine(mug, array("f", [0, 0])) == 0.0
