FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    POETRY_VERSION=2.1.4

WORKDIR /app

RUN pip install --upgrade pip \
 && pip install "poetry==${POETRY_VERSION}" \
 && poetry config virtualenvs.create false

# Copy manifests first so dependency layer is cached across source edits.
# The poetry.lock* glob tolerates the lock file being absent (first build).
COPY services/ai/pyproject.toml services/ai/poetry.lock* ./

# --no-root: the local 'app' package is not installed as a wheel; it is
#            imported from /app/app at runtime via uvicorn's working dir.
# --only main: skip the [dependency-groups] dev section (pytest, ruff, mypy).
RUN poetry install --no-interaction --no-ansi --only main --no-root

COPY services/ai/app ./app

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
