// src/FinancialAI.Infrastructure/Workflow/PersistAndRenderExecutor.cs
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class PersistAndRenderExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IReportRepository _reports;
    private readonly IOutboxRepository _outbox;

    public PersistAndRenderExecutor(
        IReportRepository reports,
        IOutboxRepository outbox)
        : base("PersistAndRender")
    {
        _reports = reports;
        _outbox = outbox;
    }

    public override async ValueTask<WorkflowState> HandleAsync(
        WorkflowState input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var report = await _reports.GetAsync(
            input.ReportId.ToString("N"), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Report {input.ReportId} disappeared before persistence.");

        if (input.AiReview is not null)
        {
            report = report with
            {
                Narrative = input.AiReview.Narrative,
                KeyRisks = input.AiReview.KeyRisks,
                CitedSources = input.AiReview.CitedSources,
                ConfidenceScore = input.AiReview.Confidence,
            };
        }

        report = report with { Status = ReportStatus.Completed.ToWireString() };

        var outboxMessage = new OutboxMessage(
            Id: Guid.NewGuid().ToString("N"),
            ReportId: report.Id,
            EventType: "report.completed",
            Payload: System.Text.Json.JsonSerializer.Serialize(
                new ReportCompleted(input.ReportId)),
            CreatedAtUtc: DateTimeOffset.UtcNow);

        // Transactional write: report state + outbox document in one batch.
        await _outbox.WriteWithReportAsync(report, outboxMessage, cancellationToken);

        // PDF is not rendered here. GET /api/reports/{id}/pdf renders on
        // demand from the persisted Report model — that is the same Report
        // object this executor just saved, so the artifact is identical.
        // Rendering here and discarding the bytes wasted work on every
        // workflow run.

        return input with
        {
            Status = "completed",
        };
    }
}
