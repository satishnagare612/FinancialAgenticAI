from typing import Any, Literal, Protocol, TypedDict

from langgraph.graph import END, StateGraph
from pydantic import BaseModel

from app.models.responses import (
    AnalysisDraft,
    RetrievedContextItem,
    ReviewResponse,
)
from app.rag.retriever import Retriever

_ANALYSIS_SYSTEM_PROMPT = "Explain projections cautiously. Cite only supplied sources."
_REVIEW_SYSTEM_PROMPT = (
    "Check numbers match deterministic projections and all claims are cited."
)

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
            "retirement projection equity returns inflation "
            "nominal adjusted balance"
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
    
        draft: AnalysisDraft = await structured_analysis.ainvoke(
            [
                {"role": "system", "content": _ANALYSIS_SYSTEM_PROMPT},
                {"role": "user", "content": user_content},
            ]
        )
        state["narrative"] = draft.narrative
        state["key_risks"] = draft.key_risks
        state["confidence"] = draft.confidence
        state["cited_sources"] = draft.cited_sources
        return state

    async def review_node(state: AgentState) -> AgentState:
        supplied_ids = {c.source_id for c in state["retrieved_context"]}
        invalid_citations = [
            c for c in state["cited_sources"] if c not in supplied_ids
        ]
        if invalid_citations:
            state["review"] = ReviewResponse(
                approved=False,
                issues=[f"Invalid citation(s): {invalid_citations}"],
                final_confidence=0.0,
                decision="revise",
            )
            state["revision_count"] += 1
            return state

        review: ReviewResponse = await structured_review.ainvoke(
            [
                {"role": "system", "content": _REVIEW_SYSTEM_PROMPT},
                {"role": "user", "content": state["narrative"] or ""},
            ]
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