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
from collections.abc import Sequence
from dataclasses import dataclass
from typing import Protocol

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
    dot = sum(x * y for x, y in zip(a, b, strict=True))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    if na == 0.0 or nb == 0.0:
        return 0.0
    return dot / (na * nb)