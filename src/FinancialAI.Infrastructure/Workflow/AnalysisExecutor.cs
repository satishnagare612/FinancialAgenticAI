using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Agents.AI.Workflows;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class AnalysisExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IAiServiceClient _aiClient;
    private readonly IReportRepository _reports;

    public AnalysisExecutor(IAiServiceClient aiClient, IReportRepository reports)
        : base("Analysis")
    {
        _aiClient = aiClient;
        _reports = reports;
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

        // Persist the narrative and citations as soon as they arrive so a
        // downstream failure does not lose the AI work.
        var existing = await _reports.GetAsync(input.ReportId.ToString("N"), cancellationToken);
        if (existing is not null)
        {
            var updated = existing with
            {
                Narrative = response.Narrative,
                KeyRisks = response.KeyRisks,
                CitedSources = response.CitedSources,
                ConfidenceScore = response.Confidence,
                // Status is deliberately not updated here: analysis happens
                // within the "running" state. The status transitions to a
                // terminal value in PersistAndRender (completed) or HumanReview
                // (needs_review). "analyzed" was an enum-less intermediate that
                // broke the frontend's polling loop.
            };
            await _reports.SaveAsync(updated, cancellationToken);
        }

        return input with
        {
            AiReview = response,
            Status = "analyzed",
        };
    }
}
