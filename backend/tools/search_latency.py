"""Times a spoken-question turn on a Gemini Live session with Google Search on or off.

Usage: uv run python -m tools.search_latency [--mode search|no-search|both] [--runs N] [QUESTION ...]
GEMINI_API_KEY, LIVE_MODEL and LIVE_VOICE are read from the environment or backend/.env.

The question is sent as realtime text, so the clock starts when it is sent, where the headset's starts when the
microphone goes quiet. Both settings share that offset, so the difference between them is what search costs.
"""
import argparse
import asyncio
import json
import statistics
import time

import httpx
import websockets

from edith_backend.agents.ask import AskRequest, LiveAsk
from edith_backend.config import Settings

URL = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent?key={key}"
QUESTIONS = ["What's the weather in Toronto right now?", "What is a mug made of?"]


async def time_turn(settings: Settings, search: bool, question: str) -> tuple[float | None, float, list[str]]:
    """Ms to the first answer audio (None if none came), ms to the end of the turn, and the grounded source titles."""
    key = settings.gemini_api_key.get_secret_value()
    ask = LiveAsk(httpx.AsyncClient(), key, settings.live_model, settings.live_voice, search)
    sources: list[str] = []
    async with websockets.connect(URL.format(key=key), max_size=None) as socket:
        await socket.send(json.dumps({"setup": ask.setup(AskRequest())}))
        while "setupComplete" not in json.loads(await socket.recv()):
            pass
        start = time.monotonic()
        await socket.send(json.dumps({"realtimeInput": {"text": question}}))
        first_audio = None
        async for raw in socket:
            content = json.loads(raw).get("serverContent", {})
            now = (time.monotonic() - start) * 1000
            for part in content.get("modelTurn", {}).get("parts", []):
                if first_audio is None and part.get("inlineData", {}).get("mimeType", "").startswith("audio/pcm"):
                    first_audio = now
            for chunk in content.get("groundingMetadata", {}).get("groundingChunks", []):
                title = chunk.get("web", {}).get("title", "")
                if title and title not in sources:
                    sources.append(title)
            if content.get("turnComplete"):
                return first_audio, now, sources
    return first_audio, (time.monotonic() - start) * 1000, sources


async def main(args: argparse.Namespace) -> None:
    settings = Settings()
    if not settings.gemini_api_key:
        raise SystemExit("GEMINI_API_KEY is not set")
    modes = {"search": [True], "no-search": [False], "both": [False, True]}[args.mode]
    for question in args.questions or QUESTIONS:
        for search in modes:
            firsts = []
            for run in range(args.runs):
                first, done, sources = await time_turn(settings, search, question)
                firsts.append(first)
                shown = "no audio" if first is None else f"{first:6.0f} ms"
                print(f"search={'on ' if search else 'off'} run {run + 1}  first audio {shown}  turn {done:6.0f} ms  "
                      f"grounded={'yes' if sources else 'no'} {sources}  | {question}")
            heard = [f for f in firsts if f is not None]
            if heard:
                print(f"search={'on ' if search else 'off'} median first audio {statistics.median(heard):.0f} ms")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mode", choices=["search", "no-search", "both"], default="both")
    parser.add_argument("--runs", type=int, default=5)
    parser.add_argument("questions", nargs="*")
    asyncio.run(main(parser.parse_args()))
