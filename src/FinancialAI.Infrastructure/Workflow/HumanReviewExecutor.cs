using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Agents.AI.Workflows;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class HumanReviewExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IReportRepository _reports;
    private readonly IHumanReviewTaskRepository _reviewTasks;

    public HumanReviewExecutor(
        IReportRepository reports,
        IHumanReviewTaskRepository reviewTasks)
        : base("HumanReview")
    {
        _reports = reports;
        _reviewTasks = reviewTasks;
    }

    public override async ValueTask<WorkflowState> HandleAsync(
        WorkflowState input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var reportId = input.ReportId.ToString("N");
        var reason = input.AiReview?.Decision == "human_review"
            ? "Reviewer confidence below threshold or revision cap reached."
            : "Manual review requested by policy.";

        // Idempotent: reuse the existing task if one is already open for
        // this report (workflow retries must not create duplicate tasks).
        var existing = await _reviewTasks.GetForReportAsync(reportId, cancellationToken);
        var reviewTask = existing is not null
            ? existing with { Reason = reason, Status = "queued" }
            : new HumanReviewTask(
                Id: Guid.NewGuid().ToString("N"),
                ReportId: reportId,
                Reason: reason,
                Status: "queued",
                CreatedAtUtc: DateTimeOffset.UtcNow);

        await _reviewTasks.SaveAsync(reviewTask, cancellationToken);

        // Mark the report as needs_review. Must be a separate save from the
        // task write; the two live in different containers, so no batch.
        var report = await _reports.GetAsync(reportId, cancellationToken);
        if (report is not null)
        {
            await _reports.SaveAsync(
                report with { Status = ReportStatus.NeedsReview.ToWireString() },
                cancellationToken);
        }

        return input with
        {
            Status = "needs_review",
            ReviewReason = reason,
        };
    }
}
