"""Runs the app under a real uvicorn socket. ASGI test transports collect the whole body first, so only a socket shows
whether events are flushed as they are produced and what happens when the client hangs up."""
import asyncio
import json
import socket
import threading
import time

import httpx
import uvicorn

from edith_backend.app import create_app
from edith_backend.events import Fact, Name

from .conftest import PROFILE, RecordingStore, look_files

STEP_S = 0.2


class SlowAgent:
    async def run(self, image, meta, profile_id):
        await asyncio.sleep(STEP_S)
        yield Name(name="a ceramic mug", confidence=0.9)
        await asyncio.sleep(STEP_S)
        yield Fact(text="Handles came late to European cups.")


def serve(settings, store):
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        port = s.getsockname()[1]
    server = uvicorn.Server(uvicorn.Config(create_app(settings, agent=SlowAgent(), store=store),
                                           host="127.0.0.1", port=port, log_level="warning"))
    thread = threading.Thread(target=server.run, daemon=True)
    thread.start()
    deadline = time.monotonic() + 5
    while not server.started:
        assert thread.is_alive() and time.monotonic() < deadline, "uvicorn did not start"
        time.sleep(0.01)
    return server, thread, f"http://127.0.0.1:{port}/look"


def test_events_reach_the_client_as_they_are_produced_and_a_hangup_after_done_still_records(settings):
    store = RecordingStore()
    server, thread, url = serve(settings, store)
    try:
        arrivals = []
        # The client is built before timing starts: creating one loads the CA bundle, which takes up to seconds.
        with httpx.Client(timeout=10) as client:
            start = time.monotonic()
            with client.stream("POST", url, files=look_files(), headers={"X-Edith-Profile": PROFILE}) as response:
                for line in response.iter_lines():
                    event = json.loads(line)
                    arrivals.append((event["type"], time.monotonic() - start))
                    if event["type"] == "done":
                        break
        assert [t for t, _ in arrivals] == ["accepted", "name", "fact", "done"]
        accepted_at, name_at = arrivals[0][1], arrivals[1][1]
        # Measured as a gap rather than from the start, which also counts connecting and uploading.
        assert name_at - accepted_at >= STEP_S * 0.5

        deadline = time.monotonic() + 2
        while not store.records and time.monotonic() < deadline:
            time.sleep(0.01)
        assert [r.status for r in store.records] == ["done"]
    finally:
        server.should_exit = True
        thread.join(5)
