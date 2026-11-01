using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class AnalysisExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IAiServiceClient _aiClient;
    private readonly IReportRepository _reports;
    private readonly ILogger<AnalysisExecutor> _logger;

    public AnalysisExecutor(IAiServiceClient aiClient, IReportRepository reports, ILogger<AnalysisExecutor> logger)
        : base("Analysis")
    {
        _aiClient = aiClient;
        _reports = reports;
        _logger = logger;
    }

    public override async ValueTask<WorkflowState> HandleAsync(
        WorkflowState input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (input.Projections is null || input.ProjectionInput is null)
        {
            throw new InvalidOperationException(
                "Projections must be calculated before analysis.");
        }

        var request = new AiServiceAnalyzeRequest(
            CorrelationId: input.CorrelationId,
            ReportId: input.ReportId.ToString("N"),
            Messages:
            [
                new AiMessage(
                    "system",
                    "Explain projections cautiously. Cite only supplied sources."),
                new AiMessage(
                    "user",
                    "Describe the projected series, flag risks, and cite each claim."),
            ],
            Projections: new ProjectionPayload(
                BaseCase: input.Projections.Select(p => p.NominalBalance).ToList(),
                ConservativeCase: input.Projections.Select(p => p.NominalBalance).ToList()),
            RetrievedContext: input.RetrievedContext ?? []);

        var response = await _aiClient.AnalyzeAsync(request, cancellationToken);

        // The report MUST exist here — CalculateProjectionsExecutor read and
        // wrote it seconds ago. If GetAsync returns null, something is
        // wrong with the Cosmos read path (wrong ID, session consistency,
        // partition key). Silently skipping the save leaves the report with
        // a null narrative, which is worse than a loud failure — the user
        // sees status=needs_review with no content to review.
        var existing = await _reports.GetAsync(
            input.ReportId.ToString("N"), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Report {input.ReportId} disappeared between CalculateProjections " +
                "and Analysis. Narrative cannot be saved.");

        var updated = existing with
        {
            Narrative = response.Narrative,
            KeyRisks = response.KeyRisks,
            CitedSources = response.CitedSources,
            ConfidenceScore = response.Confidence,
        };
        _logger.LogInformation(
            "Analysis saving narrative. ReportId={ReportId} NarrativeLength={Length}",
            input.ReportId, response.Narrative?.Length ?? 0);

        await _reports.SaveAsync(updated, cancellationToken);

        _logger.LogInformation(
            "Analysis save complete. ReportId={ReportId}", input.ReportId);

        return input with
        {
            AiReview = response,
            Status = "analyzed",
        };
    }
}
