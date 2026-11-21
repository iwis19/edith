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
    # The address the headset's pairing panel tells people to open on their phone. Empty uses this backend's own /phone,
    # which on a hosted domain can be too long to type, so it can be set to a short link that redirects there.
    phone_link: str = ""

    # Demo only: every Look's rarity steps through common to mythic in turn, whatever the model judged.
    demo_rarity_tour: bool = False

    # Which provider writes the card. Without that provider's key the stub card agent answers instead.
    card_provider: Literal["openai", "gemini"] = "openai"
    # gpt-4o names objects more specifically than gpt-5.4-mini with fewer false facts, at about 0.6 s to the first
    # name. OpenAI reportedly removes it from the API on 2027-01-14, so its replacement must be measured before then.
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
    scan_model: str = "gemini-3.5-flash-lite"
    scan_thinking_level: str = "minimal"
    # Asked as well when scan_model fails or has not answered by scan_backup_after_s; the first good answer wins.
    # Needs OPENAI_API_KEY. 0 turns the backup off.
    scan_backup_model: str = "gpt-4o"
    # Flash-Lite usually answers within 2 s, so a reply still missing then is the slow case.
    scan_backup_after_s: float = 2.0
    live_model: str = "gemini-3.8-live"
    live_voice: LiveVoice = "Kore"
    # Google Search grounding for Ask. A free-tier key refuses the Live session when the setup carries the tool.
    live_search: bool = False
    embedding_model: str = "gemini-embedding-2"
    # The size of every stored embedding; it can change only before the first one is written.
    embedding_dimensions: int = 1536
    # Two kinds of object count as one when their embeddings' cosine similarity is at least this.
    kind_similarity: float = 0.8
    # The embedding of a Look's kind is asked for beside the fact call and dropped when it takes longer, so it never
    # delays the card; the kinds then match by a shared word.
    embedding_timeout_s: float = 0.3

    stub_name_ms: int = 300
    stub_fact_ms: int = 700
    stub_rarity_ms: int = 800

    max_image_bytes: int = 2 * 1024 * 1024
    max_image_side: int = 1024
    # A Scan sends the whole camera frame, 1280x960, where a Look sends a crop.
    scan_max_image_side: int = 1280
    look_timeout_s: float = 20.0
    # The headset gives a scan 8 s, so the backend answers with its own error first.
    scan_timeout_s: float = 7.0
    db_pool_max: int = 5
