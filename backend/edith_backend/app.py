import asyncio
import json
import logging
import secrets
import time
import uuid
from collections.abc import AsyncIterator, Awaitable
from contextlib import asynccontextmanager
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID

import httpx
from fastapi import Body, Depends, FastAPI, File, Form, Header, Query, Request, Response, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import HTMLResponse, JSONResponse, StreamingResponse
from pydantic import BaseModel, Field, ValidationError
from starlette.types import ASGIApp, Message, Receive, Scope, Send

from . import VERSION
from .agents.ask import AskError, AskRequest, AskSession, LiveAsk, make_live_ask
from .agents.card import CardAgent, make_card_agent
from .agents.hunt_targets import make_target_drafter
from .agents.scan import ScanAgent, make_scan_agent
from .config import Settings
from .db import LookRecord, Store, StoreUnavailable, make_store
from .events import (
    Accepted, CollectMeta, Done, ErrorEvent, Fact, HuntEvent, HuntState, LookDetails, LookEvent, LookMeta, Name,
    RarityEvent, ScanMeta, ScanReply,
)
from .hunts import TARGET_COUNT, Hunts, can_tick, to_state
from .jpeg import is_jpeg, jpeg_size
from .location import LocationFix, Locations, LocationSummary
from .navigate import (
    NAVIGATE_TIMEOUT_S, SEARCH_FIX_S, HttpPlaceFinder, NavigateReply, NavigateRequest, Navigations, NavStatus, PlaceFinder, SearchFailed,
    others, status,
)
from .pairing import BadCode, Pairings, PhoneTokens, TooManyTries
from .resolve import HttpResolver

log = logging.getLogger("edith")
if not log.handlers:
    _handler = logging.StreamHandler()
    _handler.setFormatter(logging.Formatter("%(asctime)s %(name)s %(message)s"))
    log.addHandler(_handler)
    log.setLevel(logging.INFO)

# Room for the multipart boundaries, part headers and the meta field on top of the largest allowed image.
_FORM_OVERHEAD_BYTES = 64 * 1024

# The phone cannot send the device token, so these answer without it. Each checks its own credential: a pairing code
# with a throttle, or the phone token the code is traded for. Their bodies are a few fields, so the cap is small.
_PHONE_ROUTES = frozenset({("GET", "/phone"), ("POST", "/pair/claim"), ("POST", "/location")})
_PHONE_BODY_BYTES = 4096
_PHONE_PAGE = (Path(__file__).parent / "static" / "phone.html").read_text(encoding="utf-8")


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
        phone = (scope["method"], scope["path"]) in _PHONE_ROUTES
        max_body = _PHONE_BODY_BYTES if phone else self.max_body
        if self.token and not phone and not secrets.compare_digest(headers.get(b"x-edith-token", b""), self.token):
            await _send_error(send, 401, "bad_token", "The X-Edith-Token header is missing or wrong.")
            return
        length = headers.get(b"content-length")
        if length is not None and (not length.isdigit() or int(length) > max_body):
            await _send_error(send, 413, "body_too_large", f"The request body must be at most {max_body} bytes.")
            return

        received = 0
        exceeded = False
        replied = False

        async def capped_receive() -> Message:
            nonlocal received, exceeded
            message = await receive()
            received += len(message.get("body", b""))
            if received > max_body:
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
                                      f"The request body must be at most {max_body} bytes.")
                return
            await send(message)

        try:
            await self.app(scope, capped_receive, guarded_send)
        except _BodyTooLarge:
            if not replied:
                replied = True
                await _send_error(send, 413, "body_too_large", f"The request body must be at most {max_body} bytes.")


class PairClaim(BaseModel):
    code: str


class HuntRequest(BaseModel):
    interests: list[str] = Field(default_factory=list, max_length=10)


def create_app(
    settings: Settings | None = None,
    agent: CardAgent | None = None,
    store: Store | None = None,
    ask: LiveAsk | None = None,
    locations: Locations | None = None,
    pairings: Pairings | None = None,
    scanner: ScanAgent | None = None,
    hunts: Hunts | None = None,
    navigations: Navigations | None = None,
    finder: PlaceFinder | None = None,
) -> FastAPI:
    settings = settings or Settings()
    if settings.public_server and not settings.device_token:
        raise RuntimeError("PUBLIC_SERVER is set but DEVICE_TOKEN is empty, so anyone could spend the API keys.")
    # Made here and not per request: only the phone's position ever uses the resolver's client.
    locations = locations or Locations(HttpResolver(httpx.AsyncClient()))
    pairings = pairings or Pairings()
    navigations = navigations or Navigations()
    finder = finder or HttpPlaceFinder(httpx.AsyncClient())
    store = store or make_store(settings.database_url, settings.db_pool_max)
    agent = agent or make_card_agent(settings, locations.whereabouts, store)
    ask = ask or make_live_ask(settings)
    scanner = scanner or make_scan_agent(settings, store, locations.whereabouts)
    hunts = hunts or Hunts(store, make_target_drafter(settings), locations.whereabouts, settings.hunt_seconds)
    # Looks still streaming, by look_ref, so a /collect that arrives before the Look is recorded can wait for it. A
    # per-process map is right because the backend runs as a single replica.
    inflight: dict[UUID, asyncio.Event] = {}

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        await store.open()
        # A sleeping database can take seconds to wake, so startup does not wait for the places. Looks before the load
        # finishes go without the places from before the restart. Only the model agent remembers places.
        restore = getattr(agent, "restore_places", None)
        restoring = asyncio.create_task(restore()) if restore else None
        restoring_hunts = asyncio.create_task(hunts.restore())
        yield
        if restoring:
            restoring.cancel()
        restoring_hunts.cancel()
        await hunts.settle()
        if _recording:
            await asyncio.wait(_recording, timeout=10)
        await store.close()

    app = FastAPI(title="EDITH backend", version=VERSION, lifespan=lifespan)
    token = settings.device_token
    phone_tokens = PhoneTokens(token.get_secret_value().encode() if token else None)
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
            _stream(agent, store, settings, inflight, hunts, profile_id, parsed, data, size, received),
            media_type="application/x-ndjson",
        )

    @app.post("/scan")
    async def scan(
        image: UploadFile | None = File(None),
        meta: str | None = Form(None),
        profile_id: UUID = Depends(profile),
    ) -> ScanReply:
        received = time.monotonic()
        parsed = _parse_meta(meta, ScanMeta)
        data, _ = await _read_image(image, settings, settings.scan_max_image_side)
        if scanner is None:
            raise ApiError(503, "scan_unavailable", "Scan needs GEMINI_API_KEY on the backend.")
        try:
            async with asyncio.timeout(settings.scan_timeout_s):
                picks = await scanner.scan(data, profile_id, parsed.session, hunts.open_targets(profile_id))
        except TimeoutError:
            log.warning("scan timed out after %.1f s", settings.scan_timeout_s)
            raise ApiError(504, "scan_timeout", "The scan took too long.")
        except Exception as exc:
            log.warning("scan failed: %s: %s", type(exc).__name__, str(exc)[:200])
            raise ApiError(502, "scan_failed", "The scan could not be completed.")
        ms = round((time.monotonic() - received) * 1000)
        log.info("scan %d picks in %d ms: %s", len(picks), ms, [p.name for p in picks])
        return ScanReply(scan_ref=uuid.uuid4().hex, ms=ms, picks=picks)

    @app.post("/ask/session")
    async def ask_session(request: AskRequest | None = Body(None), profile_id: UUID = Depends(profile)) -> AskSession:
        if ask is None:
            raise ApiError(503, "ask_unavailable", "Ask needs GEMINI_API_KEY on the backend.")
        try:
            hunt = hunts.current(profile_id)
            return await ask.start(request or AskRequest(), locations.whereabouts(profile_id),
                                   to_state(hunt, hunts.now()) if hunt else None)
        except AskError as exc:
            log.warning("ask session failed: %s", exc)
            raise ApiError(502, "ask_failed", "Gemini did not give a session token.")

    @app.post("/hunt")
    async def start_hunt(request: HuntRequest | None = Body(None), profile_id: UUID = Depends(profile)) -> HuntState:
        interests = [i.strip()[:60] for i in (request.interests if request else []) if i.strip()]
        hunt = await hunts.start(profile_id, interests)
        log.info("hunt %s started", hunt.hunt_ref.hex)
        return to_state(hunt, hunts.now())

    @app.get("/hunt")
    async def get_hunt(profile_id: UUID = Depends(profile)) -> HuntState:
        hunt = hunts.current(profile_id)
        if hunt is None:
            raise ApiError(404, "no_hunt", "This profile has no active hunt.")
        return to_state(hunt, hunts.now())

    @app.post("/hunt/stop")
    async def stop_hunt(profile_id: UUID = Depends(profile)) -> HuntState:
        hunt = hunts.stop(profile_id)
        if hunt is None:
            raise ApiError(404, "no_hunt", "This profile has no active hunt.")
        return to_state(hunt, hunts.now())

    @app.get("/hunts")
    async def list_hunts(limit: int = Query(50, ge=1, le=200), profile_id: UUID = Depends(profile)) -> dict:
        now = hunts.now()
        stored = await _stored(hunts.history(profile_id, limit))
        return {"hunts": [to_state(s.hunt, now, s.memory_ids) for s in stored]}

    @app.post("/pair")
    async def pair(request: Request, profile_id: UUID = Depends(profile)) -> dict:
        record = pairings.issue(profile_id)
        return {"code": record.code, "expires_in_s": int(record.expires - record.created),
                "phone_url": settings.phone_link or str(request.base_url).rstrip("/") + "/phone"}

    @app.get("/pair/{code}")
    async def pair_status(code: str, profile_id: UUID = Depends(profile)) -> dict:
        paired, expired = pairings.status(profile_id, code)
        return {"paired": paired, "expired": expired}

    @app.post("/pair/claim")
    async def pair_claim(claim: PairClaim, request: Request) -> dict:
        ip = request.client.host if request.client else "unknown"
        try:
            profile_id = pairings.claim(claim.code.strip(), ip)
        except TooManyTries:
            raise ApiError(429, "too_many_tries", "Too many wrong codes. Wait ten minutes and try again.")
        except BadCode:
            raise ApiError(404, "bad_code", "That code is wrong, used or expired. Look at the headset for a new one.")
        phone_token, expires = phone_tokens.issue(profile_id)
        return {"phone_token": phone_token,
                "expires_at": datetime.fromtimestamp(expires, UTC).isoformat().replace("+00:00", "Z")}

    @app.get("/phone", response_class=HTMLResponse)
    async def phone() -> HTMLResponse:
        # The page holds no secret: the phone token comes from /pair/claim and stays in the phone's own storage.
        return HTMLResponse(_PHONE_PAGE, headers={
            "Cache-Control": "no-store",
            "Referrer-Policy": "no-referrer",
            "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
                                       "connect-src 'self'; base-uri 'none'; form-action 'none'",
        })

    @app.post("/location")
    async def post_location(fix: LocationFix, x_edith_phone: str | None = Header(None)) -> LocationSummary:
        profile_id = phone_tokens.verify(x_edith_phone or "")
        if profile_id is None:
            raise ApiError(401, "bad_phone_token", "The phone token is missing, wrong or expired. Pair again.")
        summary = locations.update(profile_id, fix)
        summary.navigating = navigations.active(profile_id)
        return summary

    @app.get("/location")
    async def get_location(profile_id: UUID = Depends(profile)) -> LocationSummary:
        summary = locations.summary(profile_id)
        if summary is None:
            raise ApiError(404, "no_location", "No phone position is kept for this profile.")
        return summary

    # Missing fields are left out rather than sent as null, so the headset's JsonUtility keeps its own defaults.
    @app.post("/navigate", response_model_exclude_none=True)
    async def navigate(request: NavigateRequest, profile_id: UUID = Depends(profile)) -> NavigateReply:
        """Ask's find_place tool: finds the nearest match around the phone's position and makes it the target."""
        if request.category == "any" and not (request.name or "").strip():
            raise ApiError(422, "nothing_asked", "Give a category, a name or both.")
        latest = locations.latest(profile_id, SEARCH_FIX_S)
        if latest is None:
            raise ApiError(409, "no_location", "The phone has not sent a recent position, so nothing can be searched.")
        fix, _ = latest
        try:
            # Gemini is waiting on this answer before it speaks, so a slow map service must not hold it for long.
            async with asyncio.timeout(NAVIGATE_TIMEOUT_S):
                places = await finder.find(fix.lat, fix.lon, request)
        except (SearchFailed, TimeoutError):
            raise ApiError(502, "search_failed", "The map services did not answer.")
        if not places:
            raise ApiError(404, "no_place", "Nothing like that was found within about five kilometres.")
        navigations.set(profile_id, places[0])
        return NavigateReply(target=status(places[0], latest), others=others(places))

    @app.get("/navigate", response_model_exclude_none=True)
    async def navigate_status(profile_id: UUID = Depends(profile)) -> NavStatus:
        return status(navigations.target(profile_id), locations.latest(profile_id, SEARCH_FIX_S))

    @app.delete("/navigate")
    async def navigate_stop(profile_id: UUID = Depends(profile)) -> dict:
        return {"stopped": navigations.clear(profile_id)}

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
        memory_id = await _stored(store.collect(profile_id, parsed.look_ref, data, parsed.collected_at))
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


async def _read_image(
    image: UploadFile | None, settings: Settings, max_side: int | None = None
) -> tuple[bytes, tuple[int, int]]:
    max_side = max_side or settings.max_image_side
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
    if max(size) > max_side:
        raise ApiError(422, "image_dimensions", f"The longest side must be at most {max_side} px, got {max(size)}.")
    return data, size


# Holds each record task until it finishes, since the event loop keeps only weak references to tasks.
_recording: set[asyncio.Task] = set()


async def _stream(
    agent: CardAgent,
    store: Store,
    settings: Settings,
    inflight: dict[UUID, asyncio.Event],
    hunts: Hunts,
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
    details: LookDetails | None = None
    final: LookEvent
    events = agent.run(image, meta, profile_id, hunts.open_targets(profile_id))
    ticked = False

    def hunt_line(*, last: bool) -> str | None:
        """Ticks the hunt once the final name and the tier are known, and returns the event line for a hit. It runs
        when the card agent reports its details, which come before the fact, and otherwise when the stream ends."""
        nonlocal ticked
        name, rarity = seen.get("name"), seen.get("rarity")
        if ticked or not isinstance(name, Name) or not (last or details is not None):
            return None
        ticked = True
        if not can_tick(name.name, name.confidence):
            return None
        tier = rarity.tier if isinstance(rarity, RarityEvent) else None
        hit = hunts.tick(profile_id, look_ref, details.hunt_pick if details else None, tier)
        if hit is None:
            return None
        return stamp(HuntEvent(index=hit.target.index, text=hit.target.text, found=hit.hunt.found,
                               total=TARGET_COUNT, status=hit.status))

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
                if line := hunt_line(last=True):
                    yield line
                break
            if isinstance(event, LookDetails):
                details = event
                if line := hunt_line(last=False):
                    yield line
                continue
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
    record = _record(look_ref, profile_id, meta, size, status, seen, details, elapsed_ms())
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
    details: LookDetails | None,
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
        kind=details.kind if details else None,
        place=details.place if details else None,
        photo_place=details.photo_place if details else None,
    )


app = create_app()
