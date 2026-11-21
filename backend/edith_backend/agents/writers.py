"""Card writers for each provider: OpenAI through its async SDK, Gemini over its REST API with httpx."""
import base64
import json
from collections.abc import AsyncIterator
from typing import Any

import httpx
from openai import AsyncOpenAI

GEMINI_API = "https://generativelanguage.googleapis.com/v1beta/models/"


class OpenAIWriter:
    def __init__(self, client: AsyncOpenAI, model: str, reasoning_effort: str = "") -> None:
        self._client, self._model, self._effort = client, model, reasoning_effort

    async def stream(self, system: str, context: str | None, image: bytes, ask: str) -> AsyncIterator[str]:
        messages: list[dict[str, Any]] = [{"role": "system", "content": system}]
        if context:
            messages.append({"role": "system", "content": context})
        messages.append({"role": "user", "content": [
            {"type": "image_url", "image_url": {"url": "data:image/jpeg;base64," + base64.b64encode(image).decode()}},
            {"type": "text", "text": ask},
        ]})
        # Models without reasoning, such as gpt-4o, reject the parameter.
        params: dict[str, Any] = {"reasoning_effort": self._effort} if self._effort else {}
        stream = await self._client.chat.completions.create(
            model=self._model, stream=True, max_completion_tokens=400, messages=messages, **params
        )
        refusal = ""
        async for chunk in stream:
            if not chunk.choices:
                continue
            delta = chunk.choices[0].delta
            refusal += getattr(delta, "refusal", None) or ""
            if delta.content:
                yield delta.content
        if refusal:
            raise RuntimeError(f"The model refused: {refusal[:200]}")


class GeminiError(RuntimeError):
    pass


_STOPPED = frozenset({"SAFETY", "RECITATION", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII"})


def gemini_text(chunk: dict[str, Any]) -> str:
    if reason := chunk.get("promptFeedback", {}).get("blockReason"):
        raise GeminiError(f"Gemini blocked the request: {reason}")
    out = []
    for candidate in chunk.get("candidates", []):
        if candidate.get("finishReason") in _STOPPED:
            raise GeminiError(f"Gemini stopped: {candidate['finishReason']}")
        out += [p.get("text", "") for p in candidate.get("content", {}).get("parts", []) if not p.get("thought")]
    return "".join(out)


class GeminiWriter:
    def __init__(self, client: httpx.AsyncClient, key: str, model: str, thinking_level: str) -> None:
        self._client, self._key, self._model, self._thinking = client, key, model, thinking_level

    async def stream(self, system: str, context: str | None, image: bytes, ask: str) -> AsyncIterator[str]:
        # Gemini takes one system instruction, so the context goes in as its own part, still apart from the photo.
        body = {
            "systemInstruction": {"parts": [{"text": system}] + ([{"text": context}] if context else [])},
            "contents": [{"role": "user", "parts": [
                {"inlineData": {"mimeType": "image/jpeg", "data": base64.b64encode(image).decode()}},
                {"text": ask},
            ]}],
            "generationConfig": {"maxOutputTokens": 600, "thinkingConfig": {"thinkingLevel": self._thinking}},
        }
        async with self._client.stream(
            "POST", f"{GEMINI_API}{self._model}:streamGenerateContent?alt=sse",
            headers={"x-goog-api-key": self._key}, json=body,
        ) as response:
            if response.status_code != 200:
                detail = (await response.aread())[:300].decode(errors="replace")
                raise GeminiError(f"Gemini answered {response.status_code}: {detail}")
            async for line in response.aiter_lines():
                if line.startswith("data:") and (text := gemini_text(json.loads(line[5:]))):
                    yield text
