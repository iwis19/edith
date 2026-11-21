import uuid

import pytest
from pydantic import SecretStr

from edith_backend.app import create_app
from edith_backend.location import Locations
from edith_backend.pairing import (
    CODE_LIFE_S, TOKEN_LIFE_S, BadCode, Pairings, PhoneTokens, TooManyTries,
)

from .conftest import PROFILE, client_for
from .test_location import FakeResolver

ANN = uuid.UUID(int=1)
BOB = uuid.UUID(int=2)


class Clock:
    def __init__(self):
        self.now = 1_000_000.0

    def __call__(self):
        return self.now


@pytest.fixture
def clock():
    return Clock()


@pytest.fixture
def pairings(clock):
    return Pairings(clock)


def other_than(code):
    return str((int(code) + 1) % 10_000).zfill(4)


def test_a_code_is_four_digits_and_lives_ten_minutes(pairings):
    record = pairings.issue(ANN)
    assert len(record.code) == 4 and record.code.isdigit()
    assert record.expires - record.created == CODE_LIFE_S == 600
    assert not record.claimed


def test_a_new_code_replaces_the_profiles_unclaimed_one_and_leaves_others(pairings):
    first = pairings.issue(ANN).code
    bobs = pairings.issue(BOB).code
    second = pairings.issue(ANN).code
    if first != second:
        assert pairings.status(ANN, first) == (False, True)
        with pytest.raises(BadCode):
            pairings.claim(first, "1.1.1.1")
    assert pairings.claim(bobs, "1.1.1.1") == BOB
    assert pairings.claim(second, "1.1.1.1") == ANN


def test_codes_are_unique_among_live_codes(pairings):
    assert len({pairings.issue(uuid.UUID(int=i)).code for i in range(300)}) == 300


def test_a_code_works_once(pairings):
    code = pairings.issue(ANN).code
    assert pairings.claim(code, "1.1.1.1") == ANN
    with pytest.raises(BadCode):
        pairings.claim(code, "1.1.1.1")


def test_an_expired_code_is_refused_and_reads_as_expired(pairings, clock):
    code = pairings.issue(ANN).code
    assert pairings.status(ANN, code) == (False, False)
    clock.now += CODE_LIFE_S
    assert pairings.status(ANN, code) == (False, True)
    with pytest.raises(BadCode):
        pairings.claim(code, "1.1.1.1")


def test_status_reads_paired_after_the_claim_and_only_for_the_asking_profile(pairings):
    code = pairings.issue(ANN).code
    assert pairings.status(BOB, code) == (False, True)
    pairings.claim(code, "1.1.1.1")
    assert pairings.status(ANN, code) == (True, False)
    assert pairings.status(BOB, code) == (False, True)
    assert pairings.status(ANN, "nope") == (False, True)


def test_the_sixth_try_from_one_address_is_refused_even_with_the_right_code(pairings, clock):
    code = pairings.issue(ANN).code
    for _ in range(5):
        with pytest.raises(BadCode):
            pairings.claim(other_than(code), "9.9.9.9")
    with pytest.raises(TooManyTries):
        pairings.claim(code, "9.9.9.9")
    assert pairings.claim(code, "8.8.8.8") == ANN, "another address is not locked"
    clock.now += 601
    code = pairings.issue(ANN).code
    assert pairings.claim(code, "9.9.9.9") == ANN, "the lock ends after ten minutes"


def test_ten_wrong_tries_in_total_lock_every_code(pairings):
    code = pairings.issue(ANN).code
    for i in range(10):
        with pytest.raises(BadCode):
            pairings.claim(other_than(code), f"10.0.0.{i}")
    with pytest.raises(TooManyTries):
        pairings.claim(code, "10.0.1.1")


def test_a_phone_token_round_trips_and_names_the_profile(clock):
    tokens = PhoneTokens(b"secret", clock)
    token, expires = tokens.issue(ANN)
    assert tokens.verify(token) == ANN
    assert expires == int(clock.now + TOKEN_LIFE_S)
    assert PhoneTokens(b"secret", clock).verify(token) == ANN, "a restart with the same secret keeps it valid"


def test_a_tampered_or_foreign_phone_token_is_refused(clock):
    tokens = PhoneTokens(b"secret", clock)
    token, _ = tokens.issue(ANN)
    payload, mac = token.split(".")
    forged_payload = tokens.issue(BOB)[0].split(".")[0]
    for bad in (token[:-2] + "AA", forged_payload + "." + mac, payload, "", "a.b", "...", token + "x"):
        assert tokens.verify(bad) is None
    assert PhoneTokens(b"other", clock).verify(token) is None


def test_a_phone_token_expires_after_thirty_days(clock):
    tokens = PhoneTokens(b"secret", clock)
    token, _ = tokens.issue(ANN)
    clock.now += TOKEN_LIFE_S - 1
    assert tokens.verify(token) == ANN
    clock.now += 1
    assert tokens.verify(token) is None


def test_without_a_secret_each_process_gets_its_own_key(clock):
    token, _ = PhoneTokens(None, clock).issue(ANN)
    assert PhoneTokens(None, clock).verify(token) is None


DEVICE = {"X-Edith-Token": "secret", "X-Edith-Profile": PROFILE}


@pytest.fixture
async def http(settings, store, clock):
    settings.device_token = SecretStr("secret")
    app = create_app(settings, store=store, locations=Locations(FakeResolver()), pairings=Pairings(clock))
    async with client_for(app) as c:
        yield c


async def test_the_headset_gets_a_code_the_phone_claims_and_the_headset_sees_it_paired(http):
    issued = (await http.post("/pair", headers=DEVICE)).json()
    assert len(issued["code"]) == 4
    assert issued["expires_in_s"] == 600
    assert issued["phone_url"] == "http://test/phone"
    assert (await http.get(f"/pair/{issued['code']}", headers=DEVICE)).json() == {"paired": False, "expired": False}

    claim = await http.post("/pair/claim", json={"code": issued["code"]})
    assert claim.status_code == 200
    assert claim.json()["phone_token"]
    assert claim.json()["expires_at"].endswith("Z")
    assert (await http.get(f"/pair/{issued['code']}", headers=DEVICE)).json() == {"paired": True, "expired": False}


async def test_a_configured_short_link_is_what_the_headset_is_told(settings, store):
    settings.phone_link = "https://edith.example/p"
    async with client_for(create_app(settings, store=store, locations=Locations(FakeResolver()))) as c:
        issued = (await c.post("/pair", headers=DEVICE)).json()
    assert issued["phone_url"] == "https://edith.example/p"


async def test_the_pairing_routes_the_headset_uses_need_the_device_token_and_a_profile(http):
    assert (await http.post("/pair", headers={"X-Edith-Profile": PROFILE})).status_code == 401
    assert (await http.get("/pair/1234", headers={"X-Edith-Profile": PROFILE})).status_code == 401
    assert (await http.get("/location", headers={"X-Edith-Profile": PROFILE})).status_code == 401
    no_profile = await http.post("/pair", headers={"X-Edith-Token": "secret"})
    assert (no_profile.status_code, no_profile.json()["error"]["code"]) == (400, "bad_profile")


async def test_the_phone_claims_without_the_device_token_and_wrong_codes_are_throttled(http):
    code = (await http.post("/pair", headers=DEVICE)).json()["code"]
    wrong = other_than(code)
    for _ in range(5):
        refused = await http.post("/pair/claim", json={"code": wrong})
        assert (refused.status_code, refused.json()["error"]["code"]) == (404, "bad_code")
    locked = await http.post("/pair/claim", json={"code": code})
    assert (locked.status_code, locked.json()["error"]["code"]) == (429, "too_many_tries")
    assert (await http.get(f"/pair/{code}", headers=DEVICE)).json()["paired"] is False


async def test_another_profile_cannot_read_a_codes_status(http):
    code = (await http.post("/pair", headers=DEVICE)).json()["code"]
    other = DEVICE | {"X-Edith-Profile": str(BOB)}
    assert (await http.get(f"/pair/{code}", headers=other)).json() == {"paired": False, "expired": True}


async def test_a_claim_body_that_is_not_a_code_is_refused(http):
    for body in ({"code": 1234}, {}):
        assert (await http.post("/pair/claim", json=body)).status_code == 422
