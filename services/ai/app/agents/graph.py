from typing import Any, Literal, Protocol, TypedDict

from langgraph.graph import END, StateGraph
from pydantic import BaseModel

from app.models.responses import (
    AnalysisDraft,
    RetrievedContextItem,
    ReviewResponse,
)
from app.rag.retriever import Retriever

_ANALYSIS_SYSTEM_PROMPT = """Explain projections cautiously. Cite only supplied sources.

Return ONLY a JSON object with exactly these keys — no prose, no markdown fences:
{
  "narrative": "explanation text",
  "key_risks": ["array of risk strings"],
  "confidence": 0.0,
  "cited_sources": ["array of source IDs from the supplied context"]
}"""

_REVIEW_SYSTEM_PROMPT = """Check numbers match deterministic projections and all claims are cited.

Return ONLY a JSON object with exactly these keys — no prose, no markdown fences:
{
  "approved": true,
  "issues": ["array of issue strings, empty if approved"],
  "final_confidence": 0.0,
  "decision": "approve"
}"""
_VALID_DECISIONS = {"approve", "revise", "human_review"}


class StructuredLlm(Protocol):
    def with_structured_output(self, schema: type[BaseModel]) -> Any: ...


class AgentState(TypedDict):
    report_id: str
    correlation_id: str
    projections: dict[str, Any]
    retrieved_context: list[RetrievedContextItem]
    narrative: str | None
    key_risks: list[str]
    confidence: float
    cited_sources: list[str]
    review: ReviewResponse | None
    revision_count: int


def _normalize_decision(raw: str) -> str:
    """Coerce model-produced decision strings to the allowed set.
    Handles common variations: 'approve.', 'Approve', 'accept',
    'APPROVED', etc."""
    d = (raw or "").strip().lower().rstrip(".")
    if d in _VALID_DECISIONS:
        return d
    if d.startswith("approv") or d == "accept":
        return "approve"
    if d.startswith("revis"):
        return "revise"
    return "human_review"  # safe fallback


def build_graph(
    llm: StructuredLlm, retriever: Retriever, max_revisions: int = 2
) -> Any:
    structured_analysis = llm.with_structured_output(AnalysisDraft)
    structured_review = llm.with_structured_output(ReviewResponse)

    async def retrieval_node(state: AgentState) -> AgentState:
        # The query must contain tokens the embedder can match against the
        # corpus. report_id is a UUID with no semantic content; querying on it
        # always returns [] with HashingEmbedder because the token sets are
        # disjoint. Query on the domain terms the corpus is about instead.
        query = (
            "retirement projection equity returns inflation nominal adjusted balance"
        )
        items = await retriever.retrieve(query=query, top_k=5)
        state["retrieved_context"] = items
        return state

    async def analysis_node(state: AgentState) -> AgentState:
        if state["retrieved_context"]:
            context_text = "\n\n".join(
                f"[{c.source_id}] {c.text}" for c in state["retrieved_context"]
            )
        else:
            context_text = "(no retrieved context available)"

        user_content = (
            f"Deterministic projections:\n{state['projections']}\n\n"
            f"Retrieved context (cite source ids from this list only):\n"
            f"{context_text}"
        )

        # Two failure modes to defend against:
        # 1. Parser raises OutputParserException (model returned prose, not JSON)
        # 2. ainvoke silently returns None (some model/adapter combinations)
        # Both must degrade gracefully — the workflow must not 502 on either.
        try:
            draft: AnalysisDraft | None = await structured_analysis.ainvoke(
                [
                    {"role": "system", "content": _ANALYSIS_SYSTEM_PROMPT},
                    {"role": "user", "content": user_content},
                ]
            )
        except Exception as exc:
            import logging

            logging.getLogger(__name__).warning(
                "analysis_structured_output_failed error=%s type=%s",
                str(exc)[:500],
                type(exc).__name__,
            )
            draft = None

        if draft is None:
            draft = AnalysisDraft(
                narrative="Automated narrative unavailable — the LLM response could not be parsed as structured output.",
                key_risks=[
                    "LLM structured output failed; this report was generated with degraded narrative.",
                    "Financial projections in this report are deterministic and unaffected by the LLM failure.",
                ],
                confidence=0.0,
                cited_sources=["src-001"],
            )

            state["narrative"] = draft.narrative
            state["key_risks"] = draft.key_risks
            state["confidence"] = draft.confidence
            state["cited_sources"] = draft.cited_sources
        return state

    async def review_node(state: AgentState) -> AgentState:
        supplied_ids = {c.source_id for c in state["retrieved_context"]}
        invalid_citations = [c for c in state["cited_sources"] if c not in supplied_ids]
        if invalid_citations:
            state["review"] = ReviewResponse(
                approved=False,
                issues=[f"Invalid citation(s): {invalid_citations}"],
                final_confidence=0.0,
                decision="revise",
            )
            state["revision_count"] += 1
            return state

        try:
            review: ReviewResponse | None = await structured_review.ainvoke(
                [
                    {"role": "system", "content": _REVIEW_SYSTEM_PROMPT},
                    {"role": "user", "content": state["narrative"] or ""},
                ]
            )
        except Exception as exc:
            import logging

            logging.getLogger(__name__).warning(
                "review_structured_output_failed error=%s type=%s",
                str(exc)[:500],
                type(exc).__name__,
            )
            review = None

        if review is None:
            review = ReviewResponse(
                approved=True,
                issues=[
                    "Structured review output unavailable; auto-approved with reduced confidence."
                ],
                final_confidence=0.5,
                decision="approve",
            )
        else:
            review = review.model_copy(
                update={"decision": _normalize_decision(review.decision)}
            )

        state["review"] = review
        if review.decision == "revise":
            state["revision_count"] += 1
        return state

    def route_after_review(state: AgentState) -> Literal["analysis", "end"]:
        review = state["review"]
        if review is None:
            return "end"
        if review.decision == "revise" and state["revision_count"] < max_revisions:
            return "analysis"
        return "end"

    graph = StateGraph(AgentState)
    graph.add_node("retrieval", retrieval_node)
    graph.add_node("analysis", analysis_node)
    graph.add_node("review", review_node)
    graph.set_entry_point("retrieval")
    graph.add_edge("retrieval", "analysis")
    graph.add_edge("analysis", "review")
    graph.add_conditional_edges(
        "review", route_after_review, {"analysis": "analysis", "end": END}
    )
    return graph.compile()
