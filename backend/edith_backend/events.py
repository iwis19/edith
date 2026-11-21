from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel

Rarity = Literal["common", "uncommon", "rare", "epic", "legendary", "mythic"]


class LookMeta(BaseModel):
    look_id: int
    source: Literal["pinch", "scan"]
    captured_at: datetime


class CollectMeta(BaseModel):
    look_ref: UUID


class Source(BaseModel):
    title: str
    url: str


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
    sources: list[Source]


class RarityEvent(_Event):
    type: Literal["rarity"] = "rarity"
    tier: Rarity


class Done(_Event):
    type: Literal["done"] = "done"


class ErrorEvent(_Event):
    type: Literal["error"] = "error"
    code: str
    message: str


CardEvent = Name | Fact | RarityEvent
LookEvent = Accepted | CardEvent | Done | ErrorEvent
