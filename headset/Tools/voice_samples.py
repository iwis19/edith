"""Records EDITH's welcome line in each welcome voice with Gemini text-to-speech, as 24 kHz mono WAVs.

Usage: GEMINI_API_KEY=... python Tools/voice_samples.py Assets/Resources/EdithUI/Voices [model]
Run from headset/. WelcomeController plays Assets/Resources/EdithUI/Voices/<style>.wav.
"""
import base64
import json
import os
import sys
import urllib.request
import wave

LINE = "Hi, I'm EDITH!"
VOICES = {
    "Enthusiastic": ("Puck", "Say this with big, bright, excited energy"),
    "Calm": ("Vindemiatrix", "Say this calmly and warmly, at a normal speaking pace"),
    "Light": ("Callirrhoe", "Say this in an easygoing, slightly cheeky way"),
}

out = sys.argv[1]
model = sys.argv[2] if len(sys.argv) > 2 else "gemini-2.5-flash-preview-tts"
key = os.environ["GEMINI_API_KEY"].strip()
os.makedirs(out, exist_ok=True)
for style, (voice, direction) in VOICES.items():
    body = {
        "contents": [{"parts": [{"text": f"{direction}: {LINE}"}]}],
        "generationConfig": {
            "responseModalities": ["AUDIO"],
            "speechConfig": {"voiceConfig": {"prebuiltVoiceConfig": {"voiceName": voice}}},
        },
    }
    request = urllib.request.Request(
        f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json", "x-goog-api-key": key},
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            reply = json.load(response)
    except urllib.error.HTTPError as e:
        sys.exit(f"{style}: HTTP {e.code} {e.read()[:300]!r}")
    part = reply["candidates"][0]["content"]["parts"][0]["inlineData"]
    pcm = base64.b64decode(part["data"])
    path = os.path.join(out, f"{style}.wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(24000)
        w.writeframes(pcm)
    print(f"{style}: {voice}, {len(pcm) / 48000:.1f} s, {part.get('mimeType')}")
