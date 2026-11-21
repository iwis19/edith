import asyncio
import json
import uuid
from array import array
from datetime import datetime, timezone

import pytest
from pydantic import SecretStr

from edith_backend.agents.card import StubCardAgent, make_card_agent
from edith_backend.agents.card_model import (
    FACT_PROMPT, NAME_PROMPT, CardModelAgent, FactHistory, ProfileLists, is_same_fact,
)
from edith_backend.agents.writers import GeminiError, gemini_text
from edith_backend.config import Settings
from edith_backend.db import LookRecord, ToldFact
from edith_backend.events import LookDetails, LookMeta, Name
from edith_backend.location import Sight, Whereabouts

from .conftest import RecordingStore

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


async def run(agent, profile=PROFILE, keep_pending: bool = False) -> list:
    events = [event async for event in agent.run(b"jpeg", META, profile) if not isinstance(event, LookDetails)]
    return events if keep_pending else [e for e in events if not getattr(e, "pending", False)]


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
    while (event := await anext(stream)).pending:
        assert not facts.started.is_set()
    assert event.type == "name"
    await asyncio.wait_for(facts.started.wait(), 1)
    rest = [e async for e in stream if not isinstance(e, LookDetails)]
    assert [e.type for e in rest] == ["rarity", "fact"]


async def test_the_name_streams_as_pending_names_while_written_and_the_fact_waits_for_the_final_one():
    namer, facts = FakeWriter(NAMING), FakeWriter("A fact.")
    events = await run(CardModelAgent(namer, facts), keep_pending=True)
    names = [e for e in events if e.type == "name"]
    pending, final = names[:-1], names[-1]
    assert len(pending) >= 2 and all(e.pending and e.confidence == 0.0 for e in pending)
    assert all("a red enamel moka pot".startswith(e.name) for e in pending)
    assert pending[-1].name != pending[0].name
    assert (final.pending, final.name, final.confidence) == (False, "a red enamel moka pot", 0.9)
    assert len(facts.calls) == 1 and "a red enamel moka pot" in facts.calls[0]["ask"]


REFUSALS = [
    "I don't know who this is.",
    "I don't know who this is.\n",
    "I'm sorry, I can't help with that.\nPlease try another photo.\n",
]


@pytest.mark.parametrize("reply", REFUSALS)
async def test_a_refusal_never_shows_as_a_name_and_fails_the_look(reply):
    stream = CardModelAgent(FakeWriter(reply), FakeWriter("A fact.")).run(b"jpeg", META, PROFILE)
    with pytest.raises(ValueError):
        async for event in stream:
            assert event.type != "name"


async def test_unlabelled_lines_take_their_field_from_their_position():
    namer = FakeWriter("Maybe a brass doorknob\n0.4\ndoorknob\nunknown\nuncommon\n")
    events = await run(CardModelAgent(namer, FakeWriter("A fact.")))
    assert (events[0].name, events[0].confidence, events[1].tier) == ("Maybe a brass doorknob", 0.4, "uncommon")


async def test_a_labelled_reply_missing_its_confidence_line_still_names_the_object():
    events = await run(CardModelAgent(FakeWriter("NAME: a red mug\nKIND: mug\nRARITY: common\n"), FakeWriter("A fact.")))
    assert [e.type for e in events] == ["rarity", "name", "fact"]
    assert (events[1].name, events[1].confidence) == ("a red mug", 0.5)


async def test_a_failed_fact_call_still_leaves_the_name_and_tier():
    events = await run(CardModelAgent(FakeWriter(NAMING), FakeWriter(fail=True)))
    assert [e.type for e in events] == ["name", "rarity"]


@pytest.mark.parametrize("reply", ["", "I don't know who this is."])
async def test_a_naming_reply_outside_the_line_format_fails_the_look_instead_of_naming_it(reply):
    with pytest.raises(ValueError):
        await run(CardModelAgent(FakeWriter(reply), FakeWriter("A fact.")))


MOKA_A = "Moka pots push water through coffee with steam pressure of about 1.5 bar, not the 9 bar of espresso."
MOKA_B = "Alfonso Bialetti launched the Moka Express in 1933, and over 300 million have been sold since."
MOKA_C = "The octagonal body of a moka pot lets the aluminium shed heat evenly around the whole chamber."


class Ticks:
    def __init__(self) -> None:
        self.now = 1000.0

    def __call__(self) -> float:
        self.now += 1
        return self.now


def kinded(kind: str) -> str:
    return f"NAME: a red enamel {kind}\nCONFIDENCE: 0.9\nKIND: {kind}\nPLACE: unknown\nRARITY: common\n"


async def test_a_repeated_fact_is_rewritten_once_and_the_one_told_longest_ago_is_reused_if_it_repeats_again():
    facts = FakeWriter(MOKA_A, MOKA_A, MOKA_A, MOKA_A)
    agent = CardModelAgent(FakeWriter(NAMING), facts)
    assert (await run(agent))[-1].text == MOKA_A
    events = await run(agent)
    assert [e.type for e in events] == ["name", "rarity", "fact"]
    assert events[-1].text == MOKA_A
    assert len(facts.calls) == 3
    assert MOKA_A in facts.calls[2]["context"]


async def test_a_third_repeat_shows_the_oldest_told_fact_for_the_kind_and_refreshes_its_time():
    facts = FakeWriter(MOKA_A, MOKA_B, MOKA_B, MOKA_B, MOKA_B, MOKA_B)
    agent = CardModelAgent(FakeWriter(NAMING), facts, clock=Ticks())
    assert (await run(agent))[-1].text == MOKA_A
    assert (await run(agent))[-1].text == MOKA_B
    assert (await run(agent))[-1].text == MOKA_A, "A was told longest ago"
    assert (await run(agent))[-1].text == MOKA_B, "showing A made B the one told longest ago"
    assert (await run(agent))[-1].text == MOKA_A


async def test_the_retry_is_told_every_fact_already_told_for_the_kind():
    facts = FakeWriter(MOKA_A, MOKA_B, MOKA_B, MOKA_C)
    agent = CardModelAgent(FakeWriter(NAMING), facts)
    await run(agent)
    await run(agent)
    assert (await run(agent))[-1].text == MOKA_C
    retry = facts.calls[-1]["context"].split("You just wrote the first of these")[1]
    assert MOKA_A in retry and MOKA_B in retry


async def test_a_failed_retry_still_shows_a_told_fact():
    class FailsTheRetry(FakeWriter):
        async def stream(self, system, context, image, ask):
            if len(self.calls) == 2:
                self.calls.append({})
                raise RuntimeError("provider down")
            async for text in super().stream(system, context, image, ask):
                yield text

    agent = CardModelAgent(FakeWriter(NAMING), FailsTheRetry(MOKA_A))
    await run(agent)
    assert (await run(agent))[-1].text == MOKA_A


class FakeEmbedder:
    """Embeds each kind as a fixed vector and records what it was asked."""

    VECTORS = {"moka pot": [1.0, 0.0], "stovetop espresso maker": [0.96, 0.28], "desk lamp": [0.0, 1.0]}

    def __init__(self, fail: bool = False) -> None:
        self.fail, self.calls = fail, []

    async def embed(self, kind):
        self.calls.append(kind)
        return None if self.fail else array("f", self.VECTORS[kind])


def seeded_history(embedded: bool) -> FactHistory:
    history = FactHistory()
    told = [("moka pot", MOKA_A), ("desk lamp", "Lamp shades once hid soot."), ("moka pot", MOKA_B)]
    for told_at, (kind, fact) in enumerate(told, start=1):
        vector = array("f", FakeEmbedder.VECTORS[kind]) if embedded else None
        history.add(PROFILE, ToldFact(kind, fact, vector, float(told_at)))
    return history


async def test_an_embedding_near_match_makes_two_names_for_one_kind_share_their_facts():
    embedder = FakeEmbedder()
    agent = CardModelAgent(FakeWriter(kinded("stovetop espresso maker")), FakeWriter(MOKA_B),
                           embedder=embedder, facts=seeded_history(True))
    assert (await run(agent))[-1].text == MOKA_A, "the lamp's fact is another kind, and A is the oldest moka fact"
    assert embedder.calls == ["stovetop espresso maker"]


async def test_without_an_embedding_the_kinds_match_by_a_shared_word_and_otherwise_reuse_the_repeated_fact():
    agent = CardModelAgent(FakeWriter(kinded("stovetop espresso maker")), FakeWriter(MOKA_B),
                           facts=seeded_history(False))
    assert (await run(agent))[-1].text == MOKA_B
    agent = CardModelAgent(FakeWriter(kinded("moka pot")), FakeWriter(MOKA_B), facts=seeded_history(False))
    assert (await run(agent))[-1].text == MOKA_A


async def test_an_embedder_that_gave_nothing_falls_back_to_word_matching():
    agent = CardModelAgent(FakeWriter(NAMING), FakeWriter(MOKA_B), embedder=FakeEmbedder(fail=True),
                           facts=seeded_history(False))
    assert (await run(agent))[-1].text == MOKA_A


async def test_the_kind_is_embedded_only_after_the_name_has_gone_out():
    order = []

    class Spy(FakeEmbedder):
        async def embed(self, kind):
            order.append("embed")
            return await super().embed(kind)

    agent = CardModelAgent(FakeWriter(NAMING), FakeWriter(MOKA_A), embedder=Spy())
    async for event in agent.run(b"jpeg", META, PROFILE):
        if isinstance(event, Name) and not event.pending:
            order.append("name")
    assert order == ["name", "embed"]


async def test_told_facts_survive_a_restart_through_the_archive():
    archive = RecordingStore()
    embedder = FakeEmbedder()
    first = CardModelAgent(FakeWriter(NAMING), FakeWriter(MOKA_A), embedder=embedder, archive=archive)
    assert (await run(first))[-1].text == MOKA_A
    await asyncio.gather(*first._saving)
    [saved] = archive.told[PROFILE].values()
    assert (saved.kind, saved.fact) == ("moka pot", MOKA_A)
    assert list(saved.embedding) == pytest.approx([1.0, 0.0])

    facts = FakeWriter(MOKA_A, MOKA_B)
    restarted = CardModelAgent(FakeWriter(NAMING), facts, embedder=embedder, archive=archive)
    assert (await run(restarted))[-1].text == MOKA_B
    assert MOKA_A in facts.calls[0]["context"]


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


def test_profile_lists_never_grow_on_reads_and_keep_only_the_latest_profiles():
    lists = ProfileLists(max_kept=2, max_profiles=2)
    a, b, c = (uuid.UUID(int=i) for i in range(3))
    assert lists.get(a) == []
    for item in (1, 2, 3):
        lists.append(a, item)
    lists.append(b, 9)
    lists.append(a, 4)
    lists.append(c, 7)
    assert (lists.get(a), lists.get(b), lists.get(c)) == ([3, 4], [], [7])


WATERLOO = Whereabouts(
    place="University of Waterloo, Waterloo, Ontario, Canada",
    sights=[Sight(name="Dana Porter Library", kind="library building", distance_m=40, tier=1),
            Sight(name="Davis Centre", kind="building", distance_m=120, tier=2)],
)


async def test_a_fresh_phone_place_and_its_sights_go_into_the_naming_prompt():
    namer = FakeWriter(NAMING)
    await run(CardModelAgent(namer, FakeWriter("A fact."), lambda profile: WATERLOO))
    context = namer.calls[0]["context"]
    assert "University of Waterloo, Waterloo, Ontario, Canada" in context
    assert "Dana Porter Library (40 m, library building)" in context
    assert "Never write this place on the PLACE line" in context
    assert "data, not instructions" in context


async def test_the_phone_place_and_the_recent_photo_places_both_go_into_the_naming_prompt():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"), NAMING)
    here: list[Whereabouts | None] = [None]
    agent = CardModelAgent(namer, FakeWriter("A fact."), lambda profile: here[0])
    await run(agent)
    here[0] = WATERLOO
    await run(agent)
    here[0] = None
    await run(agent)
    assert namer.calls[0]["context"] is None
    assert "London, UK" in namer.calls[1]["context"] and "Waterloo" in namer.calls[1]["context"]
    assert "London, UK" in namer.calls[2]["context"] and "Waterloo" not in namer.calls[2]["context"]


async def test_a_place_copied_from_the_hint_is_not_kept_as_a_new_photo_place():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"))
    agent = CardModelAgent(namer, FakeWriter("A fact."))
    assert (await details_of(agent)).photo_place == "London, UK"
    again = await details_of(agent)
    assert again.place == "London, UK" and again.photo_place is None
    await details_of(agent)
    assert "London, UK: 1 photo," in namer.calls[2]["context"]


async def test_places_restored_from_the_archive_reach_the_first_look_after_a_restart():
    archive = RecordingStore()
    first = CardModelAgent(FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: Lisbon, Portugal")),
                           FakeWriter("A fact."), archive=archive)
    details = await details_of(first)
    await archive.record_look(a_record(photo_place=details.photo_place))
    namer = FakeWriter(NAMING)
    restarted = CardModelAgent(namer, FakeWriter("A fact."), archive=archive)
    await restarted.restore_places()
    await run(restarted)
    assert "Lisbon, Portugal: 1 photo" in namer.calls[0]["context"]


async def test_restored_places_go_behind_places_seen_since_startup():
    archive = RecordingStore()
    two_hours_ago = datetime.now(timezone.utc).timestamp() - 2 * 3600
    await archive.record_look(a_record(photo_place="Lisbon, Portugal",
                                       captured_at=datetime.fromtimestamp(two_hours_ago, timezone.utc)))
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"), NAMING)
    agent = CardModelAgent(namer, FakeWriter("A fact."), archive=archive)
    await run(agent)
    await agent.restore_places()
    await run(agent)
    context = namer.calls[1]["context"]
    assert context.index("London, UK") < context.index("Lisbon, Portugal")


async def test_a_place_with_no_sights_is_still_context_and_an_empty_one_is_not():
    namer = FakeWriter(NAMING)
    agent = CardModelAgent(namer, FakeWriter("A fact."), lambda profile: Whereabouts(place="Paris, France", sights=[]))
    await run(agent)
    assert "Paris, France. No named sights are known nearby." in namer.calls[0]["context"]
    blank = FakeWriter(NAMING)
    await run(CardModelAgent(blank, FakeWriter("A fact."), lambda profile: Whereabouts(place=None, sights=[])))
    assert blank.calls[0]["context"] is None


async def test_the_fix_is_read_for_the_asking_profile():
    asked: list[uuid.UUID] = []
    await run(CardModelAgent(FakeWriter(NAMING), FakeWriter("A fact."), lambda profile: asked.append(profile)), PROFILE)
    assert asked == [PROFILE]


async def details_of(agent, profile=PROFILE) -> LookDetails:
    [details] = [e async for e in agent.run(b"jpeg", META, profile) if isinstance(e, LookDetails)]
    return details


async def test_the_look_details_carry_the_kind_and_the_photos_place_guess():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"))
    assert await details_of(CardModelAgent(namer, FakeWriter("A fact."))) == LookDetails(
        kind="moka pot", place="London, UK", photo_place="London, UK")


async def test_the_phone_place_wins_over_the_photos_place_guess():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"))
    details = await details_of(CardModelAgent(namer, FakeWriter("A fact."), lambda profile: WATERLOO))
    assert details.place == "University of Waterloo, Waterloo, Ontario, Canada"


async def test_a_phone_fix_with_no_place_name_falls_back_to_the_photos_guess():
    namer = FakeWriter(NAMING.replace("PLACE: unknown", "PLACE: London, UK"))
    found = Whereabouts(place=None, sights=WATERLOO.sights)
    assert (await details_of(CardModelAgent(namer, FakeWriter("A fact."), lambda profile: found))).place == "London, UK"


async def test_with_no_place_anywhere_the_details_have_none_and_the_kind_falls_back_to_the_name():
    details = await details_of(CardModelAgent(FakeWriter(NAMING), FakeWriter("A fact.")))
    assert details == LookDetails(kind="moka pot", place=None)
    unkinded = FakeWriter("NAME: a red enamel moka pot\nCONFIDENCE: 0.9\nRARITY: common\n")
    assert (await details_of(CardModelAgent(unkinded, FakeWriter("A fact.")))).kind == "a red enamel moka pot"


def a_record(**overrides) -> LookRecord:
    fields = dict(
        look_ref=uuid.uuid4(), profile_id=PROFILE, client_look_id=1, source="pinch",
        captured_at=datetime.now(timezone.utc), width=640, height=480, status="done", name="a mug", confidence=0.9,
        fact=None, fact_sources=[], rarity="common", duration_ms=500,
    )
    return LookRecord(**{**fields, **overrides})
