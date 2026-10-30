Yes — the `rag/` folder is a genuine gap, and it is the largest of the "reference exists, file does not" gaps in the Python service so far.

## What is missing

| File | Status | Referenced by |
|---|---|---|
| `rag/__init__.py` | missing | `from app.rag...` imports in `graph.py`, `main.py` |
| `rag/chunking.py` | missing | §14.6 step 3 ("chunk recursively with configurable size and overlap") |
| `rag/vector_index.py` | missing | §14.6 step 5 ("store vectors in FAISS / Azure AI Search behind a `VectorIndex` interface") |
| `rag/retriever.py` | missing | `graph.py` `build_graph(llm, retriever, ...)` — `retriever` has no concrete implementation anywhere |
| `app/__init__.py`, `models/__init__.py`, `agents/__init__.py` | missing | `poetry install` packaging; `mypy --strict` |

Two connected gaps make the service unrunnable even if `rag/` were populated:

- **`main.py`'s `lifespan` is a stub.** It yields without constructing a graph or retriever. So `app.state.graph` is never set, and the first request to `/v1/analyze` raises `AttributeError: 'State' object has no attribute 'graph'`.
- **`graph.py`'s `llm` parameter is typed `BaseChatModel`** but no LLM is ever constructed. Milestone 4 intentionally used a stub, but the stub was never committed, so nothing wires.

The four files below close the RAG gap. A minimal `lifespan` wiring and a graph type-hint change make the service run end-to-end.

---

## 1. Package markers

`services/ai/app/__init__.py`, `services/ai/app/models/__init__.py`, `services/ai/app/agents/__init__.py`:

```python
# Package marker.
```

`services/ai/app/rag/__init__.py`:

```python
"""Retrieval-augmented generation primitives.

This package intentionally depends only on the standard library and Pydantic.
Production vector stores (FAISS, Azure AI Search) and embedding providers
(OpenAI, Azure OpenAI, sentence-transformers) live behind the protocols in
``vector_index.py`` and are wired in ``main.py``.
"""
```

`poetry install` with `packages = [{ include = "app" }]` in `pyproject.toml` fails without `app/__init__.py`. The other `__init__.py` files are not strictly required by Python 3.3+ namespace packages but are required by `mypy --strict` and make imports explicit.

---

## 2. `services/ai/app/rag/chunking.py`

```python
"""Recursive text chunking for retrieval indexing.

Chunk boundaries fall on paragraph breaks when possible, otherwise on
sentence boundaries, otherwise on hard character limits. This mirrors the
"split on structure first, then size" strategy from §14.6 step 3.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Chunk:
    source_id: str
    text: str
    uri: str | None = None


def chunk_text(
    source_id: str,
    text: str,
    uri: str | None = None,
    chunk_size: int = 500,
    overlap: int = 50,
) -> list[Chunk]:
    """Split ``text`` into overlapping chunks of at most ``chunk_size`` chars.

    Tries paragraph boundaries first (``\\n\\n``), then sentence boundaries
    (``. ``, ``? ``, ``! ``), then hard character cuts. Every chunk carries
    the same ``source_id`` and ``uri`` so citations can be validated.
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")
    if overlap < 0 or overlap >= chunk_size:
        raise ValueError("overlap must be in [0, chunk_size)")

    if not text.strip():
        return []

    chunks: list[Chunk] = []
    start = 0
    while start < len(text):
        end = min(start + chunk_size, len(text))

        if end < len(text):
            for sep in ("\n\n", ". ", "? ", "! "):
                cut = text.rfind(sep, start, end)
                if cut > start:
                    end = cut + len(sep)
                    break

        chunks.append(Chunk(source_id=source_id, text=text[start:end].strip(), uri=uri))

        if end >= len(text):
            break
        start = max(end - overlap, start + 1)

    return chunks
```

---

## 3. `services/ai/app/rag/vector_index.py`

```python
"""Vector index protocol plus an in-memory implementation for dev and tests.

Production implementations (``FaissVectorIndex``, ``AzureAiSearchVectorIndex``)
are not in this module — they require additional packages (``faiss-cpu``,
``azure-search-documents``) that are not in the current pyproject.toml.
Add them when you deploy; the protocol stays unchanged.
"""

from __future__ import annotations

import hashlib
import math
import re
from dataclasses import dataclass
from typing import Protocol, Sequence

_WORD = re.compile(r"\w+", re.UNICODE)


@dataclass(frozen=True)
class IndexedChunk:
    source_id: str
    text: str
    uri: str | None
    embedding: tuple[float, ...]


class Embedder(Protocol):
    def embed(self, text: str) -> Sequence[float]: ...


class VectorIndex(Protocol):
    def add(self, chunk: IndexedChunk) -> None: ...
    def search(
        self, query_embedding: Sequence[float], top_k: int
    ) -> list[IndexedChunk]: ...


class HashingEmbedder:
    """Deterministic bag-of-words embedder. No external dependencies.

    Uses SHA-256 of each lowercased token to pick a bucket in
    ``dimensions``-dimensional space. Suitable for local dev and tests where
    real embeddings are not available; replace with a provider SDK for
    production.
    """

    def __init__(self, dimensions: int = 512) -> None:
        if dimensions <= 0:
            raise ValueError("dimensions must be positive")
        self._dimensions = dimensions

    def embed(self, text: str) -> list[float]:
        vec = [0.0] * self._dimensions
        for token in _WORD.findall(text.lower()):
            digest = hashlib.sha256(token.encode("utf-8")).digest()
            bucket = int.from_bytes(digest[:4], "big") % self._dimensions
            vec[bucket] += 1.0
        return vec


class InMemoryVectorIndex:
    """Brute-force cosine similarity over an in-process list of chunks.

    Fine for a few thousand chunks; not for production scale.
    """

    def __init__(self) -> None:
        self._chunks: list[IndexedChunk] = []

    def add(self, chunk: IndexedChunk) -> None:
        self._chunks.append(chunk)

    def search(
        self, query_embedding: Sequence[float], top_k: int
    ) -> list[IndexedChunk]:
        scored = [
            (_cosine(query_embedding, c.embedding), c) for c in self._chunks
        ]
        scored.sort(key=lambda pair: pair[0], reverse=True)
        return [c for score, c in scored[:top_k] if score > 0.0]


def _cosine(a: Sequence[float], b: Sequence[float]) -> float:
    if len(a) != len(b) or not a or not b:
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    if na == 0.0 or nb == 0.0:
        return 0.0
    return dot / (na * nb)
```

---

## 4. `services/ai/app/rag/retriever.py`

```python
"""Retriever protocol and a vector-backed implementation.

The graph in ``app/agents/graph.py`` accepts a ``Retriever`` (any object
with an async ``retrieve`` method). This module provides the production
shape: chunking + embedding + index lookup, all behind protocols.
"""

from __future__ import annotations

from typing import Protocol

from app.models.responses import RetrievedContextItem
from app.rag.chunking import Chunk
from app.rag.vector_index import Embedder, IndexedChunk, VectorIndex


class Retriever(Protocol):
    async def retrieve(
        self, query: str, top_k: int = 5
    ) -> list[RetrievedContextItem]: ...


class VectorRetriever:
    """Embeds documents into a ``VectorIndex`` and retrieves by similarity."""

    def __init__(self, index: VectorIndex, embedder: Embedder) -> None:
        self._index = index
        self._embedder = embedder

    def add_documents(self, chunks: list[Chunk]) -> None:
        for chunk in chunks:
            self._index.add(
                IndexedChunk(
                    source_id=chunk.source_id,
                    text=chunk.text,
                    uri=chunk.uri,
                    embedding=tuple(self._embedder.embed(chunk.text)),
                )
            )

    async def retrieve(
        self, query: str, top_k: int = 5
    ) -> list[RetrievedContextItem]:
        query_embedding = self._embedder.embed(query)
        results = self._index.search(query_embedding, top_k)
        return [
            RetrievedContextItem(source_id=r.source_id, text=r.text, uri=r.uri)
            for r in results
        ]
```

---

## 5. Wire the graph and retriever into `main.py`

The `lifespan` currently yields without constructing anything, so `app.state.graph` is never set. Replace it:

```python
from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.agents.graph import build_graph
from app.models.requests import AnalyzeRequest
from app.models.responses import AnalysisResponse, ReviewResponse
from app.rag.chunking import chunk_text
from app.rag.retriever import VectorRetriever
from app.rag.vector_index import HashingEmbedder, InMemoryVectorIndex


class _StubStructuredRunnable:
    """Test double for LangChain structured-output runnables."""

    def __init__(self, response_factory):
        self._factory = response_factory

    async def ainvoke(self, messages):
        return self._factory(messages)


class _StubLlm:
    """Milestone 4 test double. Replace with a real provider adapter.

    Implements only ``with_structured_output`` so ``build_graph`` can run.
    A real implementation would subclass ``BaseChatModel`` from
    ``langchain_core`` and delegate to OpenAI, Azure OpenAI, Anthropic, etc.
    """

    def with_structured_output(self, schema):
        if schema is AnalysisResponse:
            return _StubStructuredRunnable(
                lambda _msgs: AnalysisResponse(
                    narrative="Stub narrative. Replace _StubLlm with a real provider.",
                    key_risks=["Stub risk"],
                    confidence=0.92,
                    cited_sources=["src-001"],
                )
            )
        if schema is ReviewResponse:
            return _StubStructuredRunnable(
                lambda _msgs: ReviewResponse(
                    approved=True,
                    issues=[],
                    final_confidence=0.92,
                    decision="approve",
                )
            )
        raise ValueError(f"Unsupported schema: {schema!r}")


@asynccontextmanager
async def lifespan(app: FastAPI):
    index = InMemoryVectorIndex()
    retriever = VectorRetriever(index, HashingEmbedder())

    # Seed a small corpus so /v1/analyze returns citations that resolve.
    seed = (
        "Long-run equity returns have historically exceeded inflation by "
        "several percentage points. Retirement projections should disclose "
        "both nominal and inflation-adjusted balances."
    )
    retriever.add_documents(chunk_text("src-001", seed, uri="https://example.org/src-001"))

    app.state.graph = build_graph(_StubLlm(), retriever, max_revisions=2)
    app.state.retriever = retriever
    yield
```

The stub LLM is marked as a test double, matching the blueprint's Milestone 4 exit criteria ("recorded provider responses validate..."). Replacing it with a real provider adapter is a separate, smaller change (add the SDK, subclass `BaseChatModel`, wire credentials).

---

## 6. Loosen `graph.py`'s LLM type hint

`build_graph` currently types `llm: BaseChatModel`, which the stub does not satisfy and which `mypy --strict` rejects. Replace with a narrow protocol — the graph only calls `with_structured_output`:

```python
from typing import Any, Protocol


class StructuredLlm(Protocol):
    def with_structured_output(self, schema: type) -> Any: ...


def build_graph(llm: StructuredLlm, retriever, max_revisions: int = 2):
    ...
```

Everything else in `graph.py` stays the same. `retriever` is already untyped in the current file; annotating it is optional but recommended:

```python
from app.rag.retriever import Retriever

def build_graph(llm: StructuredLlm, retriever: Retriever, max_revisions: int = 2):

```

---

## 7. `services/ai/tests/test_rag.py`

```python
import pytest

from app.rag.chunking import chunk_text
from app.rag.retriever import VectorRetriever
from app.rag.vector_index import HashingEmbedder, InMemoryVectorIndex


def test_chunking_respects_size_and_overlap():
    text = "a" * 1300
    chunks = chunk_text("src", text, chunk_size=500, overlap=50)
    assert len(chunks) == 3
    assert all(len(c.text) <= 500 for c in chunks)
    assert all(c.source_id == "src" for c in chunks)


def test_chunking_rejects_invalid_params():
    with pytest.raises(ValueError):
        chunk_text("src", "x", chunk_size=0)
    with pytest.raises(ValueError):
        chunk_text("src", "x", chunk_size=100, overlap=100)


def test_empty_text_yields_no_chunks():
    assert chunk_text("src", "   ") == []


@pytest.mark.asyncio
async def test_retriever_returns_relevant_chunks_first():
    retriever = VectorRetriever(InMemoryVectorIndex(), HashingEmbedder())
    retriever.add_documents(
        chunk_text("src-retirement", "retirement equity portfolio projection")
    )
    retriever.add_documents(
        chunk_text("src-food", "recipe for chocolate cake with butter")
    )

    results = await retriever.retrieve("retirement projection", top_k=5)

    assert results, "expected at least one match"
    assert results[0].source_id == "src-retirement"
    assert all(r.uri is None for r in results)


@pytest.mark.asyncio
async def test_retriever_returns_empty_for_no_overlap():
    retriever = VectorRetriever(InMemoryVectorIndex(), HashingEmbedder())
    retriever.add_documents(chunk_text("src", "alpha beta gamma"))

    results = await retriever.retrieve("xyzzy", top_k=5)

    assert results == []
```

Add `pytest-asyncio` to the dev group, since two tests are async:

```powershell
Push-Location services/ai
poetry add --group dev pytest-asyncio
Pop-Location
```

Add the asyncio marker configuration to `pyproject.toml`:

```toml
[tool.pytest.ini_options]
testpaths = ["tests"]
pythonpath = ["."]
addopts = "-q"
asyncio_mode = "auto"
```

`asyncio_mode = "auto"` removes the need for `@pytest.mark.asyncio` on every async test, but I kept the markers above so the tests run under either mode.

---

## Verification

```powershell
Push-Location services/ai
poetry install
poetry run ruff check .
poetry run mypy app
poetry run pytest

# Start the service and confirm the graph is wired
poetry run uvicorn app.main:app --port 8000
Pop-Location

# Second terminal
$body = @{
    correlation_id = "corr-1"
    report_id = "r-1"
    projections = @{ base_case = @(1000, 1040); conservative_case = @(1000, 1010) }
    retrieved_context = @()
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post http://localhost:8000/v1/analyze `
    -ContentType "application/json" -Body $body | ConvertTo-Json -Depth 5
```

The `/v1/analyze` call should now return a populated `narrative`, `key_risks`, `confidence`, and `cited_sources`. Before this change, it returned `500 AttributeError: 'State' object has no attribute 'graph'`.

---

## For `KNOWN-ISSUES.md`

Add a single entry covering the whole cluster:

```
The Python AI service's RAG layer was never emitted. §14.6 describes the
pipeline in prose, and §3's folder tree lists rag/retriever.py and
rag/vector_index.py, but no file was ever produced. In addition:

  - main.py's lifespan is a stub and never sets app.state.graph, so
    /v1/analyze raises AttributeError on first request.
  - No LLM adapter exists. The blueprint says "add only the provider SDK
    you actually use" but does not ship a stub, so Milestone 4 cannot run
    the graph without one.
  - graph.py types llm as BaseChatModel, which is too narrow for a stub
    and rejects everything that is not a full LangChain model.

Fix applied:
  - Added app/rag/{__init__,chunking,vector_index,retriever}.py
    with protocols plus in-memory implementations for dev and tests.
  - Added __init__.py at app/, app/models/, app/agents/ for packaging.
  - Wired lifespan to construct InMemoryVectorIndex + HashingEmbedder +
    VectorRetriever + a _StubLlm, and set app.state.graph.
  - Changed build_graph's llm type to a StructuredLlm protocol.
  - Added test_rag.py and pytest-asyncio.

Production follow-up:
  - Replace HashingEmbedder with a real embedder (OpenAI, Azure OpenAI,
    sentence-transformers). Add the corresponding SDK.
  - Replace InMemoryVectorIndex with FaissVectorIndex (dev) or
    AzureAiSearchVectorIndex (prod). Both require new packages and
    are not in pyproject.toml yet.
  - Replace _StubLlm with a real BaseChatModel subclass.
```

This is the sixth "reference exists, file does not" gap, and the first one that blocks the service at runtime rather than at compile time. The pattern holds: any prose section that describes a pipeline (§14.6) or a folder tree entry without an accompanying code block is a TODO, not a spec.
