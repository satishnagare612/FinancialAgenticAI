import pytest
from pydantic import ValidationError

from app.models.responses import AnalysisResponse, ReviewResponse


def test_analysis_rejects_out_of_range_confidence():
    with pytest.raises(ValidationError):
        AnalysisResponse(narrative="n", confidence=1.5)


def test_review_rejects_unknown_decision():
    with pytest.raises(ValidationError):
        ReviewResponse(
            approved=True, final_confidence=0.9, decision="banana"  # type: ignore[arg-type]
        )
