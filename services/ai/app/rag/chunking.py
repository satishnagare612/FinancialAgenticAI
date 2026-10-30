"""Recursive text chunking for retrieval indexing.

Chunk boundaries fall on paragraph breaks when possible, otherwise on
sentence boundaries, otherwise on hard character limits.
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

        piece = text[start:end].strip()
        if piece:
            chunks.append(Chunk(source_id=source_id, text=piece, uri=uri))

        if end >= len(text):
            break
        start = max(end - overlap, start + 1)

    return chunks