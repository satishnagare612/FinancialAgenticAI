from pydantic import Field

from app.models._base import CamelModel
from app.models.responses import RetrievedContextItem


class ProjectionPayload(CamelModel):
    base_case: list[float]
    conservative_case: list[float]

class AnalyzeRequest(CamelModel):
    correlation_id: str = Field(min_length=1)
    report_id: str = Field(min_length=1)
    projections: ProjectionPayload
    retrieved_context: list[RetrievedContextItem]
