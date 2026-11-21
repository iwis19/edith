from pathlib import Path
from typing import Literal, get_args

from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict

BACKEND_DIR = Path(__file__).resolve().parent.parent

# Gemini Live's prebuilt voices. Live falls back to its default voice for a name it does not know, so an unknown name
# must be refused here instead.
LiveVoice = Literal[
    "Zephyr", "Puck", "Charon", "Kore", "Fenrir", "Leda", "Orus", "Aoede", "Callirrhoe", "Autonoe", "Enceladus",
    "Iapetus", "Umbriel", "Algieba", "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar",
    "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird", "Zubenelgenubi", "Vindemiatrix", "Sadachbia",
    "Sadaltager", "Sulafat",
]
LIVE_VOICES: tuple[str, ...] = get_args(LiveVoice)


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=BACKEND_DIR / ".env", env_file_encoding="utf-8", extra="ignore")

    gemini_api_key: SecretStr | None = None
    openai_api_key: SecretStr | None = None
    database_url: str | None = None
    # Shared with the headset build. When set, every endpoint but /health answers only requests carrying it in
    # X-Edith-Token, so nobody else can spend the API keys.
    device_token: SecretStr | None = None
    # Set by the Dockerfile: a public backend must not start without device_token.
    public_server: bool = False

    # Which provider writes the card. Without that provider's key the stub card agent answers instead.
    card_provider: Literal["openai", "gemini"] = "openai"
    # gpt-4o names objects more specifically than gpt-5.4-mini with fewer false facts, at about 0.6 s to the first
    # name.
    openai_name_model: str = "gpt-4o"
    # gpt-5.4-mini's facts beat gpt-4o's in blind comparison, at the same length and speed.
    openai_fact_model: str = "gpt-5.4-mini"
    openai_fact_reasoning_effort: str = "none"
    # Writes both the name and the fact when card_provider is gemini. On 10 Looks, gemini-3.1-flash-lite finished name
    # plus fact in a median of about 4.7 s against about 5.3 s for gemini-3.5-flash-lite, which names sooner (1.3 s vs
    # 2.4 s) but writes facts slower (4.0 s vs 2.3 s), and 3.1 named objects more specifically.
    gemini_card_model: str = "gemini-3.1-flash-lite"
    # Larger Gemini models reject "minimal" and think for seconds before writing, which is too slow for a Look.
    gemini_thinking_level: str = "minimal"
    scan_model: str = "gemini-3.5-flash"
    live_model: str = "gemini-3.8-live"
    live_voice: LiveVoice = "Kore"
    # Google Search grounding for Ask. A free-tier key refuses the Live session when the setup carries the tool.
    live_search: bool = False
    embedding_model: str = "gemini-embedding-2"
    # The size of every stored embedding; it can change only before the first one is written.
    embedding_dimensions: int = 1536

    stub_name_ms: int = 300
    stub_fact_ms: int = 700
    stub_rarity_ms: int = 800

    max_image_bytes: int = 2 * 1024 * 1024
    max_image_side: int = 1024
    look_timeout_s: float = 20.0
    db_pool_max: int = 5
