import asyncio
import json
import uuid
from datetime import UTC, datetime

import httpx
import pytest
from pydantic import SecretStr

from edith_backend.agents.scan import (
    MAX_PICKS, GeminiScanner, ScanAgent, ScanParseError, has_banned_opener, make_scan_agent, parse_picks, repair_pick,
)
from edith_backend.app import create_app
from edith_backend.db import Memory, StoreUnavailable
from edith_backend.events import ScanPick
from edith_backend.location import Sight, Whereabouts

from .conftest import PROFILE, client_for, make_jpeg, meta_json

HEADERS = {"X-Edith-Profile": PROFILE}
ME = uuid.UUID(PROFILE)
SESSION = uuid.UUID("11111111-0000-4000-8000-000000000001")
OTHER_SESSION = uuid.UUID("11111111-0000-4000-8000-000000000002")


def pick(name="a brass compass", hook="Spot which way it points.", point=(0.5, 0.5), box=(0.4, 0.4, 0.6, 0.6)):
    return ScanPick(name=name, hook=hook, point=point, box=box)


def scan_meta(**overrides) -> str:
    return json.dumps({"captured_at": "2026-11-21T10:00:00Z", "session": str(SESSION), **overrides})


def post_scan(client, image=None, meta=None, headers=HEADERS):
    files = {"image": ("frame.jpg", image if image is not None else make_jpeg(), "image/jpeg"),
             "meta": (None, meta if meta is not None else scan_meta())}
    return client.post("/scan", files=files, headers=headers)


class FakeModel:
    """Answers each call with the next batch of picks and records the context it was given."""

    def __init__(self, *batches: list[ScanPick]) -> None:
        self.batches, self.contexts = list(batches), []

    async def pick(self, system, context, image):
        self.contexts.append(context)
        return self.batches.pop(0)


class FakeStore:
    def __init__(self, memories=(), error: Exception | None = None, delay: float = 0) -> None:
        self.memories, self.error, self.delay = list(memories), error, delay

    async def list_memories(self, profile_id, limit):
        await asyncio.sleep(self.delay)
        if self.error:
            raise self.error
        return self.memories[:limit]


def memory(name: str, kind: str | None = None) -> Memory:
    return Memory(id=1, look_ref=uuid.uuid4(), name=name, kind=kind, fact=None, sources=[], rarity=None, place=None,
                  created_at=datetime.now(UTC))


def agent_app(settings, store, *batches: list[ScanPick]):
    return create_app(settings, store=store, scanner=ScanAgent(FakeModel(*batches), FakeStore()))


async def test_scan_reply_carries_the_picks_in_contract_form(settings, store):
    async with client_for(agent_app(settings, store, [pick(), pick(name="a globe", hook="Spin it once.")])) as c:
        response = await post_scan(c)
    assert response.status_code == 200
    body = response.json()
    assert set(body) == {"scan_ref", "ms", "picks"}
    assert len(body["scan_ref"]) == 32 and isinstance(body["ms"], int)
    assert [p["name"] for p in body["picks"]] == ["a brass compass", "a globe"]
    assert body["picks"][0] == {"name": "a brass compass", "hook": "Spot which way it points.",
                                "point": [0.5, 0.5], "box": [0.4, 0.4, 0.6, 0.6]}


async def test_scan_reads_a_form_with_a_quoted_boundary_and_a_typed_meta_part(settings, store):
    # .NET's MultipartFormDataContent, which the headset sends, quotes the boundary and types the text part.
    boundary = "6f0d8a2e-7c1b-4b7e-9d55-0c2a4f1e9b3a"
    image_part = (
        f"--{boundary}\r\nContent-Type: image/jpeg\r\n"
        "Content-Disposition: form-data; name=image; filename=frame.jpg; filename*=utf-8''frame.jpg\r\n\r\n"
    )
    meta_part = (
        f"\r\n--{boundary}\r\nContent-Type: text/plain; charset=utf-8\r\n"
        f"Content-Disposition: form-data; name=meta\r\n\r\n{scan_meta()}\r\n--{boundary}--\r\n"
    )
    body = image_part.encode() + make_jpeg() + meta_part.encode()
    headers = {**HEADERS, "Content-Type": f'multipart/form-data; boundary="{boundary}"'}
    async with client_for(agent_app(settings, store, [pick()])) as c:
        response = await c.post("/scan", content=body, headers=headers)
    assert response.status_code == 200
    assert len(response.json()["picks"]) == 1


async def test_zero_picks_is_a_good_reply(settings, store):
    async with client_for(agent_app(settings, store, [])) as c:
        response = await post_scan(c)
    assert (response.status_code, response.json()["picks"]) == (200, [])


async def test_scan_without_the_gemini_key_is_503(client):
    response = await post_scan(client)
    assert (response.status_code, response.json()["error"]["code"]) == (503, "scan_unavailable")


async def test_scan_needs_the_token_and_a_profile(settings, store):
    settings.device_token = SecretStr("s3cret-token")
    async with client_for(agent_app(settings, store, [pick()])) as c:
        assert (await post_scan(c)).status_code == 401
        assert (await post_scan(c, headers={"X-Edith-Token": "s3cret-token"})).status_code == 400
        assert (await post_scan(c, headers={**HEADERS, "X-Edith-Token": "s3cret-token"})).status_code == 200


async def test_scan_image_limits(settings, store):
    async with client_for(agent_app(settings, store, [], [])) as c:
        assert (await post_scan(c, image=make_jpeg(1280, 960))).status_code == 200
        too_wide = await post_scan(c, image=make_jpeg(1281, 960))
        assert (too_wide.status_code, too_wide.json()["error"]["code"]) == (422, "image_dimensions")
        not_jpeg = await post_scan(c, image=b"\x89PNG\r\n\x1a\n" + b"\x00" * 64)
        assert (not_jpeg.status_code, not_jpeg.json()["error"]["code"]) == (422, "invalid_image")


@pytest.mark.parametrize("bad", [
    "not json", "{}", '{"captured_at": "yesterday", "session": "11111111-0000-4000-8000-000000000001"}',
    '{"captured_at": "2026-11-21T10:00:00Z"}', '{"captured_at": "2026-11-21T10:00:00Z", "session": "launch-1"}',
])
async def test_scan_bad_meta_is_422(settings, store, bad):
    async with client_for(agent_app(settings, store, [])) as c:
        response = await post_scan(c, meta=bad)
    assert (response.status_code, response.json()["error"]["code"]) == (422, "invalid_meta")


async def test_look_keeps_its_smaller_image_limit(client):
    files = {"image": ("crop.jpg", make_jpeg(1280, 960), "image/jpeg"), "meta": (None, meta_json())}
    response = await client.post("/look", files=files, headers=HEADERS)
    assert (response.status_code, response.json()["error"]["code"]) == (422, "image_dimensions")


class Failing:
    def __init__(self, error: Exception) -> None:
        self._error = error

    async def scan(self, image, profile_id, session):
        raise self._error


@pytest.mark.parametrize("error", [RuntimeError("model down"), ScanParseError("not JSON")])
async def test_model_failure_is_502(settings, store, error):
    async with client_for(create_app(settings, store=store, scanner=Failing(error))) as c:
        response = await post_scan(c)
    assert (response.status_code, response.json()["error"]["code"]) == (502, "scan_failed")


async def test_a_scan_over_its_budget_is_504(settings, store):
    class Slow:
        async def scan(self, image, profile_id, session):
            await asyncio.sleep(5)

    settings.scan_timeout_s = 0.05
    async with client_for(create_app(settings, store=store, scanner=Slow())) as c:
        response = await post_scan(c)
    assert (response.status_code, response.json()["error"]["code"]) == (504, "scan_timeout")


def test_only_the_gemini_key_makes_a_scan_agent(settings, store):
    settings.openai_api_key = SecretStr("k")
    assert make_scan_agent(settings, store) is None
    settings.gemini_api_key = SecretStr("k")
    assert make_scan_agent(settings, store) is not None


def reply(*picks: dict) -> str:
    return json.dumps({"picks": list(picks)})


def test_boxes_are_ymin_xmin_ymax_xmax_on_a_1000_scale():
    [p] = parse_picks(reply({
        "name": "a vintage Olivetti typewriter", "hook": "Try a key.",
        "box_2d": [400, 300, 720, 550], "point": [550, 420],
    }))
    assert p.box == pytest.approx((0.30, 0.40, 0.55, 0.72))
    assert p.point == pytest.approx((0.42, 0.55))


def test_a_pick_without_a_point_is_pinned_at_its_box_centre():
    [p] = parse_picks(reply({"name": "a lamp", "hook": "Switch it.", "box_2d": [100, 200, 300, 400]}))
    assert p.point == pytest.approx((0.3, 0.2))


def test_the_reply_may_be_a_bare_list():
    assert len(parse_picks(json.dumps([{"name": "a lamp", "hook": "Switch it.", "box_2d": [0, 0, 500, 500]}]))) == 1


@pytest.mark.parametrize("text", ["not json", "5", '{"picks": "none"}', '{"other": []}'])
def test_unparseable_replies_raise(text):
    with pytest.raises(ScanParseError):
        parse_picks(text)


def test_malformed_picks_are_dropped_not_fatal():
    good = {"name": "a lamp", "hook": "Switch it.", "box_2d": [0, 0, 500, 500], "point": [250, 250]}
    out = parse_picks(reply(
        good, "junk", {"name": "no box", "hook": "x"}, {**good, "box_2d": [1, 2, 3]}, {**good, "box_2d": ["a", 0, 1, 1]},
        {**good, "box_2d": [True, 0, 500, 500]}, {**good, "name": None},
    ))
    assert len(out) == 1


def test_repair_clamps_orders_and_moves_the_point_into_the_box():
    p = repair_pick("a lamp", "Switch it.", (0.9, -0.5), (1.2, 0.6, 0.4, -0.1))
    assert p.box == (0.4, 0.0, 1.0, 0.6)
    assert p.point == (0.9, 0.0)


def test_repair_drops_empty_names_hooks_and_flat_boxes():
    box = (0.2, 0.2, 0.4, 0.4)
    assert repair_pick("  ", "Switch it.", None, box) is None
    assert repair_pick("a lamp", " \n", None, box) is None
    assert repair_pick("a lamp", "Switch it.", None, (0.5, 0.2, 0.5, 0.4)) is None
    assert repair_pick("a lamp", "Switch it.", None, (1.5, 0.2, 2.0, 0.4)) is None


def test_repair_cuts_a_name_to_ten_words():
    p = repair_pick("one two three four five six seven eight nine ten eleven twelve", "Hi.", None, (0, 0, 1, 1))
    assert p.name == "one two three four five six seven eight nine ten"


def test_at_most_three_picks_are_kept():
    raw = [{"name": f"thing {i}", "hook": "Spin it.", "box_2d": [0, 0, 500, 500]} for i in range(5)]
    assert len(parse_picks(reply(*raw))) == MAX_PICKS


async def test_the_same_hook_for_the_same_object_is_dropped_within_a_session():
    first, again = pick(), pick(name="A Brass  Compass", hook="spot which way it points.")
    fresh = pick(hook="It is older than it looks.")
    agent = ScanAgent(FakeModel([first], [again, fresh]), FakeStore())
    assert await agent.scan(b"", ME, SESSION) == [first]
    assert await agent.scan(b"", ME, SESSION) == [fresh]


async def test_the_same_hook_may_sit_on_another_object_or_another_profile():
    other = uuid.uuid4()
    agent = ScanAgent(FakeModel([pick()], [pick()], [pick(name="a globe")]), FakeStore())
    await agent.scan(b"", ME, SESSION)
    assert len(await agent.scan(b"", other, SESSION)) == 1
    assert len(await agent.scan(b"", ME, SESSION)) == 1


async def test_a_new_session_forgets_the_hooks_of_the_last():
    model = FakeModel([pick()], [pick()], [pick()])
    agent = ScanAgent(model, FakeStore())
    await agent.scan(b"", ME, SESSION)
    assert await agent.scan(b"", ME, OTHER_SESSION) == [pick()]
    assert await agent.scan(b"", ME, OTHER_SESSION) == []
    assert model.contexts[1] is None


async def test_the_model_is_told_what_was_said_and_what_is_collected():
    model = FakeModel([pick()], [])
    agent = ScanAgent(model, FakeStore([memory("a globe", "globe"), memory("a mug")]))
    await agent.scan(b"", ME, SESSION)
    await agent.scan(b"", ME, SESSION)
    assert "- a globe (globe)" in model.contexts[0] and "- a mug" in model.contexts[0]
    assert "Prefer other things" in model.contexts[0]
    assert "a brass compass" not in model.contexts[0]
    assert "- a brass compass: Spot which way it points." in model.contexts[1]


async def test_the_model_is_told_the_phones_place_and_sights():
    found = Whereabouts(place="Western University, London, Canada",
                        sights=[Sight(name="Middlesex College", kind="university", distance_m=120)])
    model = FakeModel([], [])
    await ScanAgent(model, FakeStore(), lambda profile: found if profile == ME else None).scan(b"", ME, SESSION)
    assert model.contexts[0].startswith("This person is at Western University, London, Canada.")
    assert "Middlesex College (120 m, university)" in model.contexts[0]
    await ScanAgent(model, FakeStore()).scan(b"", ME, SESSION)
    assert model.contexts[1] is None


@pytest.mark.parametrize("store", [
    FakeStore(error=StoreUnavailable("There is no database.")), FakeStore(error=RuntimeError("db down")),
])
async def test_a_failing_collected_lookup_does_not_fail_the_scan(store):
    model = FakeModel([pick()])
    assert await ScanAgent(model, store).scan(b"", ME, SESSION) == [pick()]
    assert model.contexts == [None]


async def test_a_slow_collected_lookup_is_not_waited_for(monkeypatch):
    monkeypatch.setattr("edith_backend.agents.scan.COLLECTED_TIMEOUT_S", 0.01)
    model = FakeModel([pick()])
    assert await ScanAgent(model, FakeStore([memory("a globe")], delay=5)).scan(b"", ME, SESSION) == [pick()]
    assert model.contexts == [None]


@pytest.mark.parametrize("hook", [
    "Check out that screen debug log.", "check the spine", "Look at the arches!", "See how it glows.",
    "Grab a quick sip.", "Read those steps.", "Take a seat.", "Notice the carved crest.", "Don't miss the spire.",
    "Don’t miss the spire.", "  LOOK, a heron.", "Check.",
])
def test_generic_openers_are_banned(hook):
    assert has_banned_opener(hook)


@pytest.mark.parametrize("hook", [
    "Spot the carved crest.", "Those worn keycaps hint at years of typing.", "Try matching its pose.",
    "Looks polished, doesn't it?", "Seeing it up close is worth it.", "Don't touch, just trace the carving.",
    "Which heron faces north?", "Takeaway: it is bronze.",
])
def test_specific_openers_pass(hook):
    assert not has_banned_opener(hook)


async def test_a_pick_with_a_banned_opener_is_dropped_and_the_rest_are_kept():
    good = pick(name="a heron fountain", hook="Spot the bronze heron facing north.")
    bad = pick(hook="Check which way it points.")
    agent = ScanAgent(FakeModel([bad, good], [bad]), FakeStore())
    assert await agent.scan(b"", ME, SESSION) == [good]
    assert await agent.scan(b"", ME, SESSION) == []


def gemini_scanner(handler) -> GeminiScanner:
    return GeminiScanner(httpx.AsyncClient(transport=httpx.MockTransport(handler)), "key", "gemini-test", "minimal")


async def test_gemini_scanner_asks_for_schema_json_and_parses_the_reply():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"], seen["key"] = str(request.url), request.headers["x-goog-api-key"]
        seen["body"] = json.loads(request.content)
        text = reply({"name": "a lamp", "hook": "Switch it.", "box_2d": [0, 0, 500, 500], "point": [100, 100]})
        return httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": text}]}}]})

    [p] = await gemini_scanner(handler).pick("SYSTEM", "CONTEXT", b"jpegbytes")
    assert p.point == pytest.approx((0.1, 0.1))
    assert seen["url"].endswith("gemini-test:generateContent") and seen["key"] == "key"
    body, config = seen["body"], seen["body"]["generationConfig"]
    assert config["responseMimeType"] == "application/json" and config["responseSchema"]["required"] == ["picks"]
    assert config["thinkingConfig"] == {"thinkingLevel": "minimal"}
    assert [part["text"] for part in body["systemInstruction"]["parts"]] == ["SYSTEM", "CONTEXT"]
    assert body["contents"][0]["parts"][0]["inlineData"]["mimeType"] == "image/jpeg"


async def test_gemini_scanner_raises_on_http_errors_blocks_and_empty_replies():
    with pytest.raises(RuntimeError, match="429"):
        await gemini_scanner(lambda r: httpx.Response(429, text="quota")).pick("S", None, b"")
    with pytest.raises(ScanParseError):
        await gemini_scanner(lambda r: httpx.Response(200, json={"candidates": []})).pick("S", None, b"")
    blocked = {"promptFeedback": {"blockReason": "SAFETY"}}
    with pytest.raises(RuntimeError, match="blocked"):
        await gemini_scanner(lambda r: httpx.Response(200, json=blocked)).pick("S", None, b"")
