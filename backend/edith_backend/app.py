import asyncio
import json
import logging
import secrets
import time
import uuid
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import Depends, FastAPI, File, Form, Header, Request, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import ValidationError
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from . import VERSION
from .agents.card import CardAgent, make_card_agent
from .config import Settings
from .db import LookRecord, Store, make_store
from .events import Accepted, Done, ErrorEvent, Fact, LookEvent, LookMeta, Name, RarityEvent
from .jpeg import is_jpeg, jpeg_size

log = logging.getLogger("edith")
if not log.handlers:
    _handler = logging.StreamHandler()
    _handler.setFormatter(logging.Formatter("%(asctime)s %(name)s %(message)s"))
    log.addHandler(_handler)
    log.setLevel(logging.INFO)

# Room for the multipart boundaries, part headers and the meta field on top of the largest allowed image.
_FORM_OVERHEAD_BYTES = 64 * 1024


class ApiError(Exception):
    def __init__(self, status: int, code: str, message: str) -> None:
        self.status, self.code, self.message = status, code, message


def _error_response(status: int, code: str, message: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"error": {"code": code, "message": message}})


class _BodyTooLarge(Exception):
    pass


async def _send_error(send: Send, status: int, code: str, message: str) -> None:
    body = json.dumps({"error": {"code": code, "message": message}}).encode()
    await send({"type": "http.response.start", "status": status,
                "headers": [(b"content-type", b"application/json"), (b"content-length", str(len(body)).encode())]})
    await send({"type": "http.response.body", "body": body})


class DeviceGate:
    """Checks the device token and caps the body size before anything reads the body. FastAPI parses a multipart form
    before it runs dependencies, so a check there would come after an unauthenticated upload had been buffered."""

    def __init__(self, app: ASGIApp, token: bytes | None, max_body: int) -> None:
        self.app, self.token, self.max_body = app, token, max_body

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http" or scope["path"] == "/health":
            await self.app(scope, receive, send)
            return
        headers = dict(scope["headers"])
        if self.token and not secrets.compare_digest(headers.get(b"x-edith-token", b""), self.token):
            await _send_error(send, 401, "bad_token", "The X-Edith-Token header is missing or wrong.")
            return
        length = headers.get(b"content-length")
        if length is not None and (not length.isdigit() or int(length) > self.max_body):
            await _send_error(send, 413, "body_too_large", f"The request body must be at most {self.max_body} bytes.")
            return

        received = 0
        exceeded = False
        replied = False

        async def capped_receive() -> Message:
            nonlocal received, exceeded
            message = await receive()
            received += len(message.get("body", b""))
            if received > self.max_body:
                exceeded = True
                raise _BodyTooLarge
            return message

        async def guarded_send(message: Message) -> None:
            # A body without Content-Length that runs over the cap fails inside the form parser, which answers 400;
            # that answer is replaced with the 413.
            nonlocal replied
            if exceeded:
                if not replied:
                    replied = True
                    await _send_error(send, 413, "body_too_large",
                                      f"The request body must be at most {self.max_body} bytes.")
                return
            await send(message)

        try:
            await self.app(scope, capped_receive, guarded_send)
        except _BodyTooLarge:
            if not replied:
                replied = True
                await _send_error(send, 413, "body_too_large", f"The request body must be at most {self.max_body} bytes.")


def create_app(settings: Settings | None = None, agent: CardAgent | None = None, store: Store | None = None) -> FastAPI:
    settings = settings or Settings()
    if settings.public_server and not settings.device_token:
        raise RuntimeError("PUBLIC_SERVER is set but DEVICE_TOKEN is empty, so anyone could spend the API keys.")
    agent = agent or make_card_agent(settings)
    store = store or make_store(settings.database_url, settings.db_pool_max)

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        await store.open()
        yield
        if _recording:
            await asyncio.wait(_recording, timeout=10)
        await store.close()

    app = FastAPI(title="EDITH backend", version=VERSION, lifespan=lifespan)
    token = settings.device_token
    app.add_middleware(DeviceGate, token=token.get_secret_value().encode() if token else None,
                       max_body=settings.max_image_bytes + _FORM_OVERHEAD_BYTES)

    @app.exception_handler(ApiError)
    async def _api_error(_: Request, exc: ApiError) -> JSONResponse:
        return _error_response(exc.status, exc.code, exc.message)

    @app.exception_handler(RequestValidationError)
    async def _validation_error(_: Request, exc: RequestValidationError) -> JSONResponse:
        return _error_response(422, "invalid_request", "The request did not match the expected shape.")

    def profile(x_edith_profile: str | None = Header(None)) -> UUID:
        try:
            return UUID(x_edith_profile or "")
        except ValueError:
            raise ApiError(400, "bad_profile", "The X-Edith-Profile header must be a UUID.")

    @app.get("/health")
    async def health() -> dict:
        return {"status": "ok", "version": VERSION, "db": await store.status()}

    @app.post("/look")
    async def look(
        image: UploadFile | None = File(None),
        meta: str | None = Form(None),
        profile_id: UUID = Depends(profile),
    ) -> StreamingResponse:
        received = time.monotonic()
        parsed = _parse_meta(meta)
        data, size = await _read_image(image, settings)
        return StreamingResponse(
            _stream(agent, store, settings, profile_id, parsed, data, size, received),
            media_type="application/x-ndjson",
        )

    return app


def _parse_meta(meta: str | None) -> LookMeta:
    if meta is None:
        raise ApiError(422, "invalid_meta", "The meta field is required.")
    try:
        return LookMeta.model_validate(json.loads(meta))
    except (json.JSONDecodeError, ValidationError) as exc:
        raise ApiError(422, "invalid_meta", f"The meta field is not valid ({type(exc).__name__}).")


async def _read_image(image: UploadFile | None, settings: Settings) -> tuple[bytes, tuple[int, int]]:
    if image is None:
        raise ApiError(422, "missing_image", "The image field is required.")
    data = await image.read(settings.max_image_bytes + 1)
    if len(data) > settings.max_image_bytes:
        raise ApiError(413, "image_too_large", f"The image must be at most {settings.max_image_bytes} bytes.")
    if not is_jpeg(data):
        raise ApiError(422, "invalid_image", "The image must be a JPEG.")
    size = jpeg_size(data)
    if size is None:
        raise ApiError(422, "invalid_image", "The JPEG dimensions could not be read.")
    if max(size) > settings.max_image_side:
        raise ApiError(422, "image_dimensions",
                       f"The longest side must be at most {settings.max_image_side} px, got {max(size)}.")
    return data, size


# Holds each record task until it finishes, since the event loop keeps only weak references to tasks.
_recording: set[asyncio.Task] = set()


async def _stream(
    agent: CardAgent,
    store: Store,
    settings: Settings,
    profile_id: UUID,
    meta: LookMeta,
    image: bytes,
    size: tuple[int, int],
    received: float,
) -> AsyncIterator[str]:
    def elapsed_ms() -> int:
        return round((time.monotonic() - received) * 1000)

    def stamp(event: LookEvent) -> str:
        event.t_ms = elapsed_ms()
        return event.model_dump_json() + "\n"

    look_ref = uuid.uuid4()
    marks: dict[str, int] = {}
    seen: dict[str, LookEvent] = {}
    final: LookEvent
    events = agent.run(image, meta, profile_id)
    # The budget is spent only while waiting on the agent. Time suspended at a yield belongs to the client, and a
    # timeout firing there would cancel the response's send instead of the agent.
    loop = asyncio.get_running_loop()
    budget = settings.look_timeout_s
    try:
        accepted = Accepted(look_id=meta.look_id, look_ref=look_ref.hex)
        yield stamp(accepted)
        marks["accepted"] = accepted.t_ms
        while True:
            waited_from = loop.time()
            async with asyncio.timeout(budget):
                event = await anext(events, None)
            budget -= loop.time() - waited_from
            if event is None:
                break
            line = stamp(event)
            marks.setdefault(event.type, event.t_ms)
            seen[event.type] = event
            yield line
    except (GeneratorExit, asyncio.CancelledError):
        log.info("look %s abandoned by the client after %s ms", meta.look_id, elapsed_ms())
        raise
    except TimeoutError:
        final = ErrorEvent(code="timeout", message="The look took too long.")
    except Exception:
        log.exception("look %s failed", meta.look_id)
        final = ErrorEvent(code="agent_failed", message="The look could not be completed.")
    else:
        final = Done()
    finally:
        if hasattr(events, "aclose"):
            await events.aclose()
    status = "done" if isinstance(final, Done) else "error"
    # Started before the last event goes out: the headset hangs up once it reads that event, which cancels anything
    # this generator still awaits.
    record = _record(look_ref, profile_id, meta, size, status, seen, elapsed_ms())
    task = asyncio.create_task(store.record_look(record))
    _recording.add(task)
    task.add_done_callback(_recording.discard)
    last = stamp(final)
    log.info("look %s accepted %s name %s fact %s %s %s", meta.look_id, marks.get("accepted", "-"),
             marks.get("name", "-"), marks.get("fact", "-"), final.type, final.t_ms)
    yield last


def _record(
    look_ref: UUID,
    profile_id: UUID,
    meta: LookMeta,
    size: tuple[int, int],
    status: str,
    seen: dict[str, LookEvent],
    duration_ms: int,
) -> LookRecord:
    name, fact, rarity = seen.get("name"), seen.get("fact"), seen.get("rarity")
    return LookRecord(
        look_ref=look_ref,
        profile_id=profile_id,
        client_look_id=meta.look_id,
        source=meta.source,
        captured_at=meta.captured_at,
        width=size[0],
        height=size[1],
        status=status,
        name=name.name if isinstance(name, Name) else None,
        confidence=name.confidence if isinstance(name, Name) else None,
        fact=fact.text if isinstance(fact, Fact) else None,
        fact_sources=[s.model_dump() for s in fact.sources] if isinstance(fact, Fact) else [],
        rarity=rarity.tier if isinstance(rarity, RarityEvent) else None,
        duration_ms=duration_ms,
    )


app = create_app()
