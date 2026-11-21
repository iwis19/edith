import uuid
from datetime import UTC, datetime

from edith_backend.agents.card import RarityTour, StubCardAgent, make_card_agent
from edith_backend.config import Settings
from edith_backend.events import LookMeta, RarityEvent

META = LookMeta(look_id=1, source="pinch", captured_at=datetime.now(UTC))


async def tier_of(agent) -> str:
    async for event in agent.run(b"", META, uuid.uuid4()):
        if isinstance(event, RarityEvent):
            return event.tier
    raise AssertionError("no rarity event")


async def test_the_tour_steps_through_every_tier_in_turn():
    settings = Settings(stub_name_ms=0, stub_fact_ms=0, stub_rarity_ms=0)
    agent = RarityTour(StubCardAgent(settings))

    seen = [await tier_of(agent) for _ in range(7)]

    assert seen == ["common", "uncommon", "rare", "epic", "legendary", "mythic", "common"]


def test_the_tour_is_off_unless_asked_for():
    assert not isinstance(make_card_agent(Settings(openai_api_key=None, gemini_api_key=None)), RarityTour)
    assert isinstance(make_card_agent(Settings(openai_api_key=None, gemini_api_key=None, demo_rarity_tour=True)), RarityTour)
