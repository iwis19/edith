import asyncio
import json
import uuid
from datetime import datetime, timezone

import pytest
from pydantic import SecretStr

from edith_backend.agents.card import StubCardAgent, make_card_agent
from edith_backend.agents.card_model import FACT_PROMPT, NAME_PROMPT, CardModelAgent, is_same_fact
from edith_backend.agents.writers import GeminiError, gemini_text
from edith_backend.config import Settings
from edith_backend.events import LookMeta

PROFILE = uuid.UUID("6f1c2a4e-0000-4000-8000-000000000001")
META = LookMeta(look_id=1, source="pinch", captured_at=datetime.now(timezone.utc))
NAMING = "NAME: a red enamel moka pot\nCONFIDENCE: 0.9\nKIND: moka pot\nPLACE: unknown\nRARITY: common\n"


class FakeWriter:
    """Streams scripted replies in small chunks: one per call, the last repeated. Records what each call was given."""

    def __init__(self, *replies: str, fail: bool = False) -> None:
        self.replies, self.fail = list(replies), fail
        self.calls: list[dict] = []
        self.started = asyncio.Event()

    async def stream(self, system, context, image, ask):
        self.calls.append({"system": system, "context": context, "ask": ask})
        self.started.set()
        if self.fail:
            raise RuntimeError("provider down")
        reply = self.replies[min(len(self.calls), len(self.replies)) - 1]
        for i in range(0, len(reply), 7):
            await asyncio.sleep(0)
            yield reply[i:i + 7]


async def run(agent, profile=PROFILE) -> list:
    return [event async for event in agent.run(b"jpeg", META, profile)]


async def test_streams_the_name_then_the_tier_then_a_fact_written_about_that_name():
    namer, facts = FakeWriter(NAMING), FakeWriter("The 1933 Bialetti design copied an aluminium laundry boiler.")
    events = await run(CardModelAgent(namer, facts))
    assert [e.type for e in events] == ["name", "rarity", "fact"]
    assert (events[0].name, events[0].confidence) == ("a red enamel moka pot", 0.9)
    assert events[1].tier == "common"
    assert events[2].text == "The 1933 Bialetti design copied an aluminium laundry boiler."
    assert namer.calls[0]["system"] == NAME_PROMPT
    assert facts.calls[0]["system"] == FACT_PROMPT
    assert "a red enamel moka pot" in facts.calls[0]["ask"]


async def test_the_fact_call_starts_once_the_name_is_final_not_after_the_whole_naming_reply():
    namer, facts = FakeWriter(NAMING), FakeWriter("A fact.")
    agent = CardModelAgent(namer, facts)
    stream = agent.run(b"jpeg", META, PROFILE)
    first = await anext(stream)
    assert first.type == "name"
    await asyncio.wait_for(facts.started.wait(), 1)
    rest = [e async for e in stream]
    assert [e.type for e in rest] == ["rarity", "fact"]


async def test_unlabelled_lines_take_their_field_from_their_position():
    namer = FakeWriter("Maybe a brass doorknob\n0.4\ndoorknob\nunknown\nuncommon\n")
    events = await run(CardModelAgent(namer, FakeWriter("A fact.")))
    assert (events[0].name, events[0].confidence, events[1].tier) == ("Maybe a brass doorknob", 0.4, "uncommon")


async def test_a_failed_fact_call_still_leaves_the_name_and_tier():
    events = await run(CardModelAgent(FakeWriter(NAMING), FakeWriter(fail=True)))
    assert [e.type for e in events] == ["name", "rarity"]


@pytest.mark.parametrize("reply", ["", "I don't know who this is."])
async def test_a_naming_reply_outside_the_line_format_fails_the_look_instead_of_naming_it(reply):
    with pytest.raises(ValueError):
        await run(CardModelAgent(FakeWriter(reply), FakeWriter("A fact.")))


async def test_a_repeated_fact_is_rewritten_once_and_dropped_if_it_repeats_again():
    told = "Moka pots push water through coffee with steam pressure of about 1.5 bar, not the 9 bar of espresso."
    facts = FakeWriter(told, told, told, told)
    agent = CardModelAgent(FakeWriter(NAMING), facts)
    assert (await run(agent))[-1].text == told
    events = await run(agent)
    assert [e.type for e in events] == ["name", "rarity"]
    assert len(facts.calls) == 3
    assert told in facts.calls[2]["context"]


async def test_fact_history_is_kept_per_profile():
    told = "Moka pots push water through coffee with steam pressure of about 1.5 bar, not the 9 bar of espresso."
    agent = CardModelAgent(FakeWriter(NAMING), FakeWriter(told))
    await run(agent)
    other = await run(agent, uuid.UUID("6f1c2a4e-0000-4000-8000-000000000002"))
    assert other[-1].text == told


async def test_a_place_the_photo_showed_becomes_context_for_later_looks():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"), NAMING)
    agent = CardModelAgent(namer, FakeWriter("A fact."))
    await run(agent)
    await run(agent)
    assert namer.calls[0]["context"] is None
    assert "London, UK" in namer.calls[1]["context"]


def test_retellings_of_one_fact_match_and_different_facts_about_one_object_do_not():
    ignore = frozenset({"moka", "pot"})
    a = "Alfonso Bialetti launched the moka pot in 1933, and over 300 million have been sold since."
    assert is_same_fact(a, "Since Alfonso Bialetti launched it in 1933, over 300 million moka pots have sold.", ignore)
    assert not is_same_fact(a, "A moka pot brews at about 1.5 bar, far below the 9 bar of espresso machines.", ignore)


def test_gemini_text_skips_thoughts_and_raises_on_a_block():
    chunk = {"candidates": [{"content": {"parts": [{"text": "plan", "thought": True}, {"text": "NAME: a mug"}]}}]}
    assert gemini_text(chunk) == "NAME: a mug"
    with pytest.raises(GeminiError):
        gemini_text({"promptFeedback": {"blockReason": "SAFETY"}})
    with pytest.raises(GeminiError):
        gemini_text(json.loads('{"candidates": [{"finishReason": "SAFETY"}]}'))


@pytest.mark.parametrize("provider, keys, expected", [
    ("openai", {"openai_api_key": SecretStr("sk-test")}, CardModelAgent),
    ("gemini", {"gemini_api_key": SecretStr("g-test")}, CardModelAgent),
    ("openai", {"gemini_api_key": SecretStr("g-test")}, StubCardAgent),
    ("gemini", {}, StubCardAgent),
])
def test_the_card_provider_picks_the_agent_and_a_missing_key_falls_back_to_the_stub(provider, keys, expected):
    assert type(make_card_agent(Settings(_env_file=None, card_provider=provider, **keys))) is expected
