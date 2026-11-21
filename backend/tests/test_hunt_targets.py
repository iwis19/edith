import asyncio
import json
import random

import httpx
import pytest
from pydantic import SecretStr

from edith_backend.agents import hunt_targets
from edith_backend.agents.hunt_targets import FALLBACK_THEMES, make_target_drafter, repair
from edith_backend.hunts import TARGET_COUNT


REAL_CLIENT = httpx.AsyncClient


def rng() -> random.Random:
    return random.Random(7)


def assert_valid(targets) -> None:
    assert len(targets) == TARGET_COUNT
    assert [t.index for t in targets] == list(range(TARGET_COUNT))
    texts = [t.text.casefold() for t in targets]
    assert len(set(texts)) == TARGET_COUNT
    tiers = [t.tier for t in targets if t.tier]
    assert 1 <= len(tiers) <= 2
    assert all(t in ("uncommon", "rare") for t in tiers)
    assert all(t.tier is None or t.text == f"something {t.tier.capitalize()} or better" for t in targets)
    assert all(len(t.text.split()) <= hunt_targets.MAX_WORDS for t in targets)


def reply(*items) -> dict:
    return {"targets": [{"text": text, "tier": tier} for text, tier in items]}


def test_a_good_reply_is_kept_with_the_rarity_targets_last():
    targets = repair(reply(("something Rare or better", "rare"), ("something red", None), ("something round", None),
                           ("something with wheels", None), ("something Uncommon or better", "uncommon")), rng())
    assert_valid(targets)
    assert [t.text for t in targets] == ["something red", "something round", "something with wheels",
                                         "something Uncommon or better", "something Rare or better"]


def test_a_tier_above_rare_is_capped_and_common_is_not_a_target():
    targets = repair(reply(("a", None), ("b", None), ("c", None), ("d", None), ("something Legendary or better", "legendary")),
                     rng())
    assert_valid(targets)
    assert [t.tier for t in targets if t.tier] == ["rare"]
    common = repair(reply(("something red", None), ("something Common or better", "common")), rng())
    assert_valid(common)
    assert not any("Common" in t.text for t in common)


def test_duplicates_extras_and_long_texts_are_repaired_to_exactly_five():
    messy = reply(*[("Something RED", None), ("something red", None), ("something round and very shiny and big", None)]
                  + [(f"theme {i}", None) for i in range(6)]
                  + [("something Rare or better", "rare"), ("another rare", "rare"), ("uncommon one", "uncommon"),
                     ("third", "uncommon")])
    targets = repair(messy, rng())
    assert_valid(targets)
    assert sum(t.tier is not None for t in targets) == 2
    assert targets[0].text == "Something RED"
    assert targets[1].text == "something round and very shiny and"


@pytest.mark.parametrize("bad", [None, "text", [], {}, {"targets": "x"}, {"targets": [1, None, {"text": 5}]},
                                 reply(("only one", None))])
def test_an_unusable_reply_is_filled_from_the_built_in_pool(bad):
    targets = repair(bad, rng())
    assert_valid(targets)
    themes = [t.text for t in targets if t.tier is None]
    assert all(t in FALLBACK_THEMES or t == "only one" for t in themes)


async def test_the_drafter_falls_back_without_a_key(settings):
    targets = await make_target_drafter(settings, rng())(None, [])
    assert_valid(targets)


async def test_the_gemini_call_carries_the_place_and_interests_and_its_targets_are_used(settings, monkeypatch):
    seen: list[httpx.Request] = []
    good = reply(("something red", None), ("something round", None), ("something with wheels", None),
                 ("something Uncommon or better", "uncommon"), ("something Rare or better", "rare"))

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": json.dumps(good)}]}}]})

    monkeypatch.setattr(httpx, "AsyncClient", lambda *a, **k: REAL_CLIENT(transport=httpx.MockTransport(handler)))
    settings.gemini_api_key = SecretStr("g-key")
    drafter = make_target_drafter(settings, rng())
    targets = await drafter("This person is at Waterloo.", ["bikes", "birds"])
    assert [t.text for t in targets] == ["something red", "something round", "something with wheels",
                                         "something Uncommon or better", "something Rare or better"]
    body = json.loads(seen[0].content)
    assert seen[0].headers["x-goog-api-key"] == "g-key"
    assert f"{settings.gemini_card_model}:generateContent" in str(seen[0].url)
    context = body["systemInstruction"]["parts"][1]["text"]
    assert "Waterloo" in context and "bikes; birds" in context
    assert body["generationConfig"]["responseMimeType"] == "application/json"


@pytest.mark.parametrize("failure", ["status", "garbage", "timeout"])
async def test_a_failed_or_slow_call_still_gives_five_targets(settings, monkeypatch, failure):
    async def handler(request: httpx.Request) -> httpx.Response:
        if failure == "timeout":
            await asyncio.sleep(5)
        if failure == "status":
            return httpx.Response(500, text="down")
        return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": "not json"}]}}]})

    monkeypatch.setattr(httpx, "AsyncClient", lambda *a, **k: REAL_CLIENT(transport=httpx.MockTransport(handler)))
    settings.gemini_api_key = SecretStr("g-key")
    settings.hunt_targets_timeout_s = 0.1
    assert_valid(await make_target_drafter(settings, rng())(None, []))
