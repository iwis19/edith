"""Pairs a phone with a headset's profile. The headset shows a short-lived code, the phone trades it for a signed token,
and that token is the only credential the phone's own endpoints accept."""
import base64
import hashlib
import hmac
import json
import secrets
import time
from collections import deque
from collections.abc import Callable
from dataclasses import dataclass
from uuid import UUID

CODE_LIFE_S = 600
TOKEN_LIFE_S = 30 * 24 * 3600
# Wrong codes counted per client address and in total, over this window.
TRY_WINDOW_S = 600
MAX_WRONG_PER_IP = 5
MAX_WRONG_TOTAL = 10


@dataclass
class PairingCode:
    code: str
    profile_id: UUID
    created: float
    expires: float
    claimed: bool = False


class TooManyTries(Exception):
    pass


class BadCode(Exception):
    pass


class PhoneTokens:
    """Stateless phone credential: urlsafe(payload) + "." + urlsafe(HMAC-SHA256 of the payload)."""

    def __init__(self, key: bytes | None, clock: Callable[[], float] = time.time) -> None:
        # With no DEVICE_TOKEN (local development) a random per-process key is used, so tokens die on restart.
        self._key = key or secrets.token_bytes(32)
        self._clock = clock

    def _mac(self, payload: bytes) -> bytes:
        return hmac.new(self._key, b"edith-phone-v1." + payload, hashlib.sha256).digest()

    def issue(self, profile_id: UUID) -> tuple[str, int]:
        expires = int(self._clock() + TOKEN_LIFE_S)
        payload = json.dumps({"p": str(profile_id), "e": expires}, separators=(",", ":")).encode()
        return f"{_b64(payload)}.{_b64(self._mac(payload))}", expires

    def verify(self, token: str) -> UUID | None:
        try:
            payload_part, mac_part = token.split(".")
            payload, mac = _unb64(payload_part), _unb64(mac_part)
            if not hmac.compare_digest(mac, self._mac(payload)):
                return None
            claims = json.loads(payload)
            profile_id, expires = UUID(claims["p"]), claims["e"]
        except (ValueError, KeyError, TypeError):
            return None
        if not isinstance(expires, int) or expires <= self._clock():
            return None
        return profile_id


class Pairings:
    """Pairing codes held in memory: the backend runs as one replica and a code lives ten minutes."""

    def __init__(self, clock: Callable[[], float] = time.time) -> None:
        self._clock = clock
        self._codes: dict[str, PairingCode] = {}
        self._wrong_by_ip: dict[str, deque[float]] = {}
        self._wrong_total: deque[float] = deque()

    def issue(self, profile_id: UUID) -> PairingCode:
        now = self._clock()
        self._drop_expired(now)
        # A claimed code stays until it expires, so the headset can still read that it was paired.
        for old in [c for c in self._codes.values() if c.profile_id == profile_id and not c.claimed]:
            del self._codes[old.code]
        while (code := f"{secrets.randbelow(10_000):04d}") in self._codes:
            pass
        record = PairingCode(code, profile_id, now, now + CODE_LIFE_S)
        self._codes[code] = record
        return record

    def status(self, profile_id: UUID, code: str) -> tuple[bool, bool]:
        """Returns (paired, expired). Another profile's code reads as expired."""
        record = self._codes.get(code)
        if record is None or record.profile_id != profile_id or record.expires <= self._clock():
            return False, True
        return record.claimed, False

    def claim(self, code: str, ip: str) -> UUID:
        """Returns the profile the code was issued to, and uses the code up."""
        now = self._clock()
        self._drop_expired(now)
        mine = self._recent(self._wrong_by_ip.setdefault(ip, deque()), now)
        total = self._recent(self._wrong_total, now)
        if len(mine) >= MAX_WRONG_PER_IP or len(total) >= MAX_WRONG_TOTAL:
            raise TooManyTries
        record = self._codes.get(code)
        if record is None or record.claimed:
            mine.append(now)
            total.append(now)
            raise BadCode
        record.claimed = True
        return record.profile_id

    def _recent(self, tries: deque[float], now: float) -> deque[float]:
        while tries and tries[0] <= now - TRY_WINDOW_S:
            tries.popleft()
        return tries

    def _drop_expired(self, now: float) -> None:
        for code in [c for c, r in self._codes.items() if r.expires <= now]:
            del self._codes[code]
        for ip in [ip for ip, tries in self._wrong_by_ip.items() if not self._recent(tries, now)]:
            del self._wrong_by_ip[ip]


def _b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def _unb64(text: str) -> bytes:
    return base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))
