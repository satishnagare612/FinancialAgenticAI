import uuid
from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI, HTTPException
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import Response

from app.agents.graph import build_graph
from app.config import resolve_llm_config, settings
from app.llm.provider import build_real_llm
from app.llm.stub import StubLlm
from app.models.requests import AnalyzeRequest
from app.models.responses import (
    AnalysisResponse,
)
from app.rag.chunking import chunk_text
from app.rag.retriever import VectorRetriever
from app.rag.vector_index import HashingEmbedder, InMemoryVectorIndex

structlog.configure(
    processors=[
        structlog.contextvars.merge_contextvars,
        structlog.processors.add_log_level,
        structlog.processors.TimeStamper(fmt="iso", utc=True),
        structlog.processors.JSONRenderer(),
    ],
    wrapper_class=structlog.make_filtering_bound_logger(0),
    cache_logger_on_first_use=True,
)

logger = structlog.get_logger(__name__)




@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:

    index = InMemoryVectorIndex()
    retriever = VectorRetriever(index, HashingEmbedder())

    seed = (
        "Long-run equity returns have historically exceeded inflation by "
        "several percentage points. Retirement projections should disclose "
        "both nominal and inflation-adjusted balances."
    )
    retriever.add_documents(chunk_text("src-001", seed, uri="https://example.org/src-001"))

    if settings.llm_provider == "real":
        base_url, model, structured_method = resolve_llm_config()
        llm = build_real_llm(
            base_url=base_url,
            api_key=settings.llm_api_key,
            model=model,
            structured_method=structured_method,
            temperature=settings.llm_temperature,
            timeout_seconds=settings.llm_timeout_seconds,
            max_retries=settings.llm_max_retries,
        )
    else:
        llm = StubLlm()
    app.state.graph = build_graph(llm, retriever, max_revisions=2)
    app.state.llm_provider = settings.llm_provider
    app.state.retriever = retriever
    yield


class CorrelationMiddleware(BaseHTTPMiddleware):
    async def dispatch(
        self,
        request: Request,
        call_next: Callable[[Request], Awaitable[Response]],
    ) -> Response:
        correlation_id = request.headers.get("X-Correlation-Id") or str(uuid.uuid4())
        structlog.contextvars.bind_contextvars(correlation_id=correlation_id)
        try:
            response = await call_next(request)
            response.headers["X-Correlation-Id"] = correlation_id
            return response
        finally:
            structlog.contextvars.unbind_contextvars("correlation_id")


app = FastAPI(title="FinancialAI AI Service", lifespan=lifespan)

app.add_middleware(CorrelationMiddleware)


@app.get("/healthz")
async def healthz() -> dict[str, str]:
    return {"status": "healthy"}


@app.get("/readyz")
async def readyz() -> dict[str, str]:
    return {"status": "ready"}


@app.post("/v1/analyze", response_model=AnalysisResponse)
async def analyze(payload: AnalyzeRequest) -> AnalysisResponse:
    logger.info(
        "analyze_started",
        report_id=payload.report_id,
        correlation_id=payload.correlation_id,
    )

    graph = app.state.graph

    try:
        result = await graph.ainvoke(
            {
                "report_id": payload.report_id,
                "correlation_id": payload.correlation_id,
                "projections": payload.projections.model_dump(),
                "retrieved_context": list(payload.retrieved_context),
                "narrative": None,
                "key_risks": [],
                "confidence": 0.0,
                "cited_sources": [],
                "review": None,
                "revision_count": 0,
            }
        )
    except HTTPException:
        raise
    except Exception as exc:
        logger.exception(
            "analyze_failed",
            report_id=payload.report_id,
            correlation_id=payload.correlation_id,
        )
        raise HTTPException(
            status_code=502,
            detail={
                "code": "ai_service_error",
                "correlationId": payload.correlation_id,
            },
        ) from exc

    review = result.get("review")
    if review is None:
        logger.error(
            "analyze_no_review",
            report_id=payload.report_id,
            correlation_id=payload.correlation_id,
        )
        raise HTTPException(
            status_code=500,
            detail=(f"Graph terminated without a review decision (report_id={payload.report_id})."),
        )

    logger.info(
        "analyze_completed",
        report_id=payload.report_id,
        correlation_id=payload.correlation_id,
        decision=review.decision,
        confidence=result["confidence"],
    )

    return AnalysisResponse(
        narrative=result["narrative"] or "",
        key_risks=result["key_risks"],
        confidence=result["confidence"],
        cited_sources=result["cited_sources"],
        decision=review.decision,
    )





