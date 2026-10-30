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