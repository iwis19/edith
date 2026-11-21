FROM python:3.13-slim
COPY --from=ghcr.io/astral-sh/uv:0.13.9 /uv /usr/local/bin/uv

WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy
COPY pyproject.toml uv.lock .python-version ./
RUN uv sync --locked --no-dev --no-install-project
COPY edith_backend ./edith_backend
RUN useradd --system --no-create-home --uid 10001 edith
USER edith

ENV PATH="/app/.venv/bin:$PATH" PUBLIC_SERVER=1
EXPOSE 8000
CMD ["sh", "-c", "exec uvicorn edith_backend.app:app --host 0.0.0.0 --port ${PORT:-8000} --proxy-headers --forwarded-allow-ips='*'"]
