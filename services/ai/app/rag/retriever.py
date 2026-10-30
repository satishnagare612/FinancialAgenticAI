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