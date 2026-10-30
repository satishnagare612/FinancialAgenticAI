"""Deterministic test double for StructuredLlm (LLM_PROVIDER=stub)."""

from collections.abc import Callable
from typing import Any

from pydantic import BaseModel

from app.models.responses import AnalysisDraft, ReviewResponse


class StubStructuredRunnable:
    def __init__(self, response_factory: Callable[[Any], BaseModel]) -> None:
        self._factory = response_factory

    async def ainvoke(self, messages: Any) -> Any:
        return self._factory(messages)


class StubLlm:
    def with_structured_output(
        self, schema: type[BaseModel]
    ) -> StubStructuredRunnable:
        if schema is AnalysisDraft:
            return StubStructuredRunnable(
                lambda _msgs: AnalysisDraft(
                    narrative="Stub narrative. Replace _StubLlm with a real provider.",
                    key_risks=[
                        "Inflation uncertainty may reduce real returns",
                        "Equity market volatility can affect nominal balances",
                    ],
                    confidence=0.92,
                    cited_sources=["src-001"],
                )
            )
        if schema is ReviewResponse:
            return StubStructuredRunnable(
                lambda _msgs: ReviewResponse(
                    approved=True,
                    issues=[],
                    final_confidence=0.92,
                    decision="approve",
                )
            )
        raise ValueError(f"Unsupported schema: {schema!r}")
