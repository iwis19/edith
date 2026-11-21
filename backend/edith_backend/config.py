from pathlib import Path

from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict

BACKEND_DIR = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=BACKEND_DIR / ".env", env_file_encoding="utf-8", extra="ignore")

    gemini_api_key: SecretStr | None = None
    database_url: str | None = None
    # Shared with the headset build. When set, every endpoint but /health answers only requests carrying it in
    # X-Edith-Token, so nobody else can spend the API keys.
    device_token: SecretStr | None = None
    # Set by the Dockerfile: a public backend must not start without device_token.
    public_server: bool = False

    card_model: str = "gemini-3.5-flash-lite"
    scan_model: str = "gemini-3.5-flash"
    live_model: str = "gemini-3.8-live"
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
