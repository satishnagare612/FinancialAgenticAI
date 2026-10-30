from typing import Literal

from pydantic import Field

from app.models._base import CamelModel


class RetrievedContextItem(CamelModel):
    source_id: str
    text: str
    uri: str | None = None

class AnalysisDraft(CamelModel):
    """LLM structured output from the analysis node.

    No ``decision`` field — the LLM produces prose, the review node produces
    the decision, and ``/v1/analyze`` composes both into AnalysisResponse.
    """

    narrative: str
    key_risks: list[str] = Field(default_factory=list)
    confidence: float = Field(ge=0.0, le=1.0)
    cited_sources: list[str] = Field(default_factory=list)

class AnalysisResponse(CamelModel):
    """Wire response returned by /v1/analyze."""

    narrative: str
    key_risks: list[str] = Field(default_factory=list)
    confidence: float = Field(ge=0.0, le=1.0)
    cited_sources: list[str] = Field(default_factory=list)
    decision: Literal["approve", "revise", "human_review"]

class ReviewResponse(CamelModel):
    """LLM structured output from the review node."""

    approved: bool
    issues: list[str] = Field(default_factory=list)
    final_confidence: float = Field(ge=0.0, le=1.0)
    decision: Literal["approve", "revise", "human_review"]