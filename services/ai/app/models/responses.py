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

    ``narrative`` has ``min_length=20`` so an empty string from a lazy model
    is rejected at pydantic validation and propagates as a ValidationError —
    which the analysis node's try/except catches, triggering the fallback.
    Without this, a model returning ``{"narrative": "", ...}`` produces a
    structurally valid but semantically empty draft that silently flows
    through the workflow.
    """

    narrative: str = Field(min_length=20)
    key_risks: list[str] = Field(default_factory=list)
    confidence: float = Field(ge=0.0, le=1.0)
    cited_sources: list[str] = Field(default_factory=list)


class AnalysisResponse(CamelModel):
    """Wire response returned by /v1/analyze.

    ``decision`` is ``Literal`` here on purpose: by the time this schema is
    constructed, ``review_node`` has already run ``_normalize_decision`` on
    the raw model output, so the value is guaranteed canonical. The Literal
    constraint is a defensive check at the wire boundary.
    """

    narrative: str
    key_risks: list[str] = Field(default_factory=list)
    confidence: float = Field(ge=0.0, le=1.0)
    cited_sources: list[str] = Field(default_factory=list)
    decision: Literal["approve", "revise", "human_review"]


class ReviewResponse(CamelModel):
    """LLM structured output from the review node.

    ``decision`` is ``str`` here, NOT ``Literal``. Models frequently return
    variants like ``"approve."``, ``"Approve"``, ``"APPROVED"``, or
    ``"accept"``. A ``Literal`` constraint would reject these at pydantic
    validation — before ``_normalize_decision`` in ``graph.py`` can coerce
    them — forcing the fallback to fire even when the model's intent was
    correct. Keep this as ``str``.
    """

    approved: bool
    issues: list[str] = Field(default_factory=list)
    final_confidence: float = Field(ge=0.0, le=1.0)
    decision: str  # NOT Literal — see docstring above
