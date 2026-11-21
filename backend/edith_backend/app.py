import asyncio
import json
import logging
import secrets
import time
import uuid
from collections.abc import AsyncIterator, Awaitable
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import Body, Depends, FastAPI, File, Form, Header, Query, Request, Response, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel, ValidationError
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from . import VERSION
from .agents.ask import AskError, AskRequest, AskSession, LiveAsk, make_live_ask
from .agents.card import CardAgent, make_card_agent
from .config import Settings
from .db import LookRecord, Store, StoreUnavailable, make_store
from .events import Accepted, CollectMeta, Done, ErrorEvent, Fact, LookEvent, LookMeta, Name, RarityEvent
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


def create_app(
    settings: Settings | None = None,
    agent: CardAgent | None = None,
    store: Store | None = None,
    ask: LiveAsk | None = None,
) -> FastAPI:
    settings = settings or Settings()
    if settings.public_server and not settings.device_token:
        raise RuntimeError("PUBLIC_SERVER is set but DEVICE_TOKEN is empty, so anyone could spend the API keys.")
    agent = agent or make_card_agent(settings)
    store = store or make_store(settings.database_url, settings.db_pool_max)
    ask = ask or make_live_ask(settings)
    # Looks still streaming, by look_ref, so a /collect that arrives before the Look is recorded can wait for it. A
    # per-process map is right because the backend runs as a single replica.
    inflight: dict[UUID, asyncio.Event] = {}

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
        parsed = _parse_meta(meta, LookMeta)
        data, size = await _read_image(image, settings)
        return StreamingResponse(
            _stream(agent, store, settings, inflight, profile_id, parsed, data, size, received),
            media_type="application/x-ndjson",
        )

    @app.post("/ask/session")
    async def ask_session(request: AskRequest | None = Body(None), _: UUID = Depends(profile)) -> AskSession:
        if ask is None:
            raise ApiError(503, "ask_unavailable", "Ask needs GEMINI_API_KEY on the backend.")
        try:
            return await ask.start(request or AskRequest())
        except AskError as exc:
            log.warning("ask session failed: %s", exc)
            raise ApiError(502, "ask_failed", "Gemini did not give a session token.")

    @app.post("/collect")
    async def collect(
        image: UploadFile | None = File(None),
        meta: str | None = Form(None),
        profile_id: UUID = Depends(profile),
    ) -> dict:
        parsed = _parse_meta(meta, CollectMeta)
        data, _ = await _read_image(image, settings)
        # A card can be pocketed before its Look has finished, and the memory should hold what arrives after.
        if (recording := inflight.get(parsed.look_ref)) is not None:
            try:
                await asyncio.wait_for(recording.wait(), settings.look_timeout_s + 5)
            except TimeoutError:
                pass
        memory_id = await _stored(store.collect(profile_id, parsed.look_ref, data))
        if memory_id is None:
            raise ApiError(404, "unknown_look", "This profile has no finished Look with that look_ref.")
        return {"memory_id": memory_id}

    @app.get("/memories")
    async def memories(limit: int = Query(100, ge=1, le=500), profile_id: UUID = Depends(profile)) -> dict:
        found = await _stored(store.list_memories(profile_id, limit))
        return {"memories": [
            {"id": m.id, "look_ref": m.look_ref.hex, "name": m.name, "kind": m.kind, "fact": m.fact,
             "sources": m.sources, "rarity": m.rarity, "place": m.place, "created_at": m.created_at.isoformat()}
            for m in found
        ]}

    @app.get("/memories/{memory_id}/image")
    async def memory_image(memory_id: int, profile_id: UUID = Depends(profile)) -> Response:
        data = await _stored(store.memory_image(profile_id, memory_id))
        if data is None:
            raise ApiError(404, "unknown_memory", "This profile has no memory with that id.")
        return Response(data, media_type="image/jpeg")

    return app


async def _stored[T](call: Awaitable[T]) -> T:
    try:
        return await call
    except StoreUnavailable:
        raise ApiError(503, "db_unavailable", "The memory store is not available right now.")


def _parse_meta[M: BaseModel](meta: str | None, model: type[M]) -> M:
    if meta is None:
        raise ApiError(422, "invalid_meta", "The meta field is required.")
    try:
        return model.model_validate(json.loads(meta))
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
    inflight: dict[UUID, asyncio.Event],
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
    recorded = inflight[look_ref] = asyncio.Event()

    def release() -> None:
        recorded.set()
        inflight.pop(look_ref, None)

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
            # The first name event, pending or not, is when the card's first words appear.
            marks.setdefault(event.type, event.t_ms)
            if isinstance(event, Name) and event.pending:
                yield line
                continue
            if isinstance(event, Name):
                marks["name_final"] = event.t_ms
            seen[event.type] = event
            yield line
    except (GeneratorExit, asyncio.CancelledError):
        log.info("look %s abandoned by the client after %s ms", meta.look_id, elapsed_ms())
        release()
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
    task.add_done_callback(lambda _: release())
    last = stamp(final)
    log.info("look %s accepted %s name %s final %s fact %s %s %s", meta.look_id, marks.get("accepted", "-"),
             marks.get("name", "-"), marks.get("name_final", "-"), marks.get("fact", "-"), final.type, final.t_ms)
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
