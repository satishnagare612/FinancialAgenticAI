using FinancialAI.Application.Contracts;
using FinancialAI.Application.Projections;

namespace FinancialAI.Application.Workflow;

public sealed record WorkflowState(
    Guid ReportId,
    string CorrelationId,
    string UserId,
    ProjectionInput? ProjectionInput,
    IReadOnlyList<ProjectionPoint>? Projections,
    IReadOnlyList<RetrievedContextItem>? RetrievedContext,
    AiServiceAnalyzeResponse? AiReview,
    ReportWorkflowOptions Options)
{
    /// <summary>
    /// The projection horizon the caller requested, in years. Set from
    /// <see cref="Contracts.ReportRequestDto.ProjectionYears"/> in
    /// ReportEndpoints.MapPost. Consumed by LoadProfileAndValidateExecutor.
    /// The default of 20 is a safety net for tests and any consumer that
    /// forgot to set it; production code must set it explicitly.
    /// </summary>
    public int ProjectionYears { get; init; } = 20;

    public int RevisionCount { get; init; }
    public string? Status { get; init; }
    /// <summary>
    /// Reason a workflow stopped at a human-review decision. Populated by
    /// HumanReviewExecutor on the needs_review path. Distinct from
    /// <see cref="Error"/> — a needs_review workflow is not a failure.
    /// </summary>
    public string? ReviewReason { get; init; }

    /// <summary>Failure text only. Set by ReportWorkflowWorker on exception.</summary>
    public string? Error { get; init; }
}
