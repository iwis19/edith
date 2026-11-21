"""Sends one Look to a running backend and prints when each event arrives, to show that it streams end to end.

Usage: uv run python -m tools.stream_check URL PHOTO.jpg
DEVICE_TOKEN is read from the environment or backend/.env.
"""
import json
import sys
import time
import uuid
from datetime import datetime, timezone

import httpx

from edith_backend.config import Settings


def main(url: str, photo: str) -> None:
    image = open(photo, "rb").read()
    meta = {"look_id": 1, "source": "pinch", "captured_at": datetime.now(timezone.utc).isoformat()}
    token = Settings().device_token
    headers = {"X-Edith-Profile": str(uuid.uuid4())}
    if token:
        headers["X-Edith-Token"] = token.get_secret_value()
    with httpx.Client(timeout=30) as client:
        start = time.monotonic()
        with client.stream("POST", url.rstrip("/") + "/look", headers=headers,
                           files={"image": ("crop.jpg", image, "image/jpeg"), "meta": (None, json.dumps(meta))}) as response:
            if response.status_code != 200:
                sys.exit(f"{response.status_code}: {response.read().decode(errors='replace')}")
            for line in response.iter_lines():
                event = json.loads(line)
                arrived = round((time.monotonic() - start) * 1000)
                detail = {k: v for k, v in event.items() if k not in ("type", "t_ms")}
                print(f"{arrived:>6} ms  {event['type']:<8} server {event['t_ms']:>5} ms  {json.dumps(detail)[:110]}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(*sys.argv[1:])
