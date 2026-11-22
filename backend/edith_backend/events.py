from datetime import datetime
from typing import Annotated, Literal
from uuid import UUID

from pydantic import AwareDatetime, BaseModel, Field, StringConstraints

Rarity = Literal["common", "uncommon", "rare", "epic", "legendary", "mythic"]
# The interests picked in the headset's Welcome. They reach the prompts, so the size is bounded.
Interests = Annotated[list[Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=40)]],
                      Field(max_length=12)]


class LookMeta(BaseModel):
    look_id: int
    source: Literal["pinch", "scan"]
    captured_at: datetime
    interests: Interests = []


class CollectMeta(BaseModel):
    look_ref: UUID
    collected_at: AwareDatetime | None = None


class ScanMeta(BaseModel):
    captured_at: datetime
    # Made by the headset once per launch. Hooks are remembered for one session at a time.
    session: UUID
    interests: Interests = []


class ScanPick(BaseModel):
    """One thing worth a look. Coordinates are fractions of the upright image: origin top-left, y down."""

    name: str
    hook: str
    point: tuple[float, float]
    box: tuple[float, float, float, float]


class ScanReply(BaseModel):
    scan_ref: str
    ms: int
    picks: list[ScanPick]


class _Event(BaseModel):
    t_ms: int = 0


class Accepted(_Event):
    type: Literal["accepted"] = "accepted"
    look_id: int
    look_ref: str


class Name(_Event):
    """A pending name is the text written so far, sent so the card can show it early; the final one follows."""

    type: Literal["name"] = "name"
    name: str
    confidence: float
    pending: bool = False


class Fact(_Event):
    type: Literal["fact"] = "fact"
    text: str


class RarityEvent(_Event):
    type: Literal["rarity"] = "rarity"
    tier: Rarity


class Done(_Event):
    type: Literal["done"] = "done"


class ErrorEvent(_Event):
    type: Literal["error"] = "error"
    code: str
    message: str


class LookDetails(BaseModel):
    """What the Card agent learned about the Look that the headset does not need: it is recorded with the Look and never
    sent down the stream."""

    kind: str
    place: str | None = None
    # The place the photo itself showed, kept for later Looks. None when the model only copied the hint it was given.
    photo_place: str | None = None


StreamedEvent = Name | Fact | RarityEvent
CardEvent = StreamedEvent | LookDetails
LookEvent = Accepted | StreamedEvent | Done | ErrorEvent
