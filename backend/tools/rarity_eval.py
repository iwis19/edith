"""Compares the naming prompt's RARITY tiers at a git ref with the working tree's, on a folder of cropped Look photos.

    uv run python tools/rarity_eval.py PHOTOS_DIR [--ref origin/main] [--limit 60] [--out results.jsonl]

Needs OPENAI_API_KEY. Each photo gets one naming call per prompt with the configured name model and no place context.
Prints each photo's name and both tiers, then the tier counts."""
import argparse
import ast
import asyncio
import json
import random
import subprocess
from collections import Counter
from pathlib import Path

from openai import AsyncOpenAI

from edith_backend.agents.card_model import NAME_ASK, NAME_PROMPT, TIERS, LineParser, parse_tier
from edith_backend.agents.writers import OpenAIWriter
from edith_backend.config import Settings

CARD_MODEL = "backend/edith_backend/agents/card_model.py"


def prompt_at(ref: str) -> str:
    source = subprocess.run(["git", "show", f"{ref}:{CARD_MODEL}"], capture_output=True, check=True).stdout.decode("utf-8")
    for node in ast.parse(source).body:
        if isinstance(node, ast.Assign) and any(getattr(t, "id", "") == "NAME_PROMPT" for t in node.targets):
            return ast.literal_eval(node.value)
    raise SystemExit(f"NAME_PROMPT not found at {ref}")


async def name(writer: OpenAIWriter, prompt: str, image: bytes) -> dict[str, str]:
    parser, fields = LineParser(), {}
    try:
        async for text in writer.stream(prompt, None, image, NAME_ASK):
            fields.update(parser.feed(text))
        fields.update(parser.finish())
    except Exception as exc:
        return {"NAME": f"<error: {exc}>"[:80]}
    return fields


async def main() -> None:
    args = argparse.ArgumentParser()
    args.add_argument("photos", type=Path)
    args.add_argument("--ref", default="origin/main")
    args.add_argument("--limit", type=int, default=60)
    args.add_argument("--seed", type=int, default=1)
    args.add_argument("--out", type=Path)
    opts = args.parse_args()

    settings = Settings()
    writer = OpenAIWriter(AsyncOpenAI(api_key=settings.openai_api_key.get_secret_value(), max_retries=2),
                          settings.openai_name_model)
    photos = sorted(p for p in opts.photos.glob("*.jp*g") if not p.stem.endswith("-full"))
    random.Random(opts.seed).shuffle(photos)
    photos = photos[: opts.limit]
    old_prompt = prompt_at(opts.ref)
    gate = asyncio.Semaphore(8)

    async def one(photo: Path) -> dict:
        image = photo.read_bytes()
        async with gate:
            old, new = await asyncio.gather(name(writer, old_prompt, image), name(writer, NAME_PROMPT, image))
        return {"photo": photo.name, "name": new.get("NAME", "-"), "old_name": old.get("NAME", "-"),
                "old": parse_tier(old.get("RARITY", "")), "new": parse_tier(new.get("RARITY", ""))}

    rows = await asyncio.gather(*(one(p) for p in photos))
    rank = {t: i for i, t in enumerate(TIERS)}
    for row in sorted(rows, key=lambda r: (rank.get(r["new"], -1), r["name"]), reverse=True):
        moved = "" if row["old"] == row["new"] else "  <- was " + str(row["old"])
        print(f"{str(row['new']):10} {row['name'][:70]}{moved}")
    for label in ("old", "new"):
        counts = Counter(r[label] for r in rows)
        print(f"{label}: " + ", ".join(f"{t} {counts[t]}" for t in TIERS if counts[t]))
    if opts.out:
        opts.out.write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")


if __name__ == "__main__":
    asyncio.run(main())
