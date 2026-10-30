using FinancialAI.Application.Contracts;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class ReviewExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IOptions<ReportWorkflowOptions> _options;

    public ReviewExecutor(IOptions<ReportWorkflowOptions> options)
        : base("Review")
    {
        _options = options;
    }

    public override ValueTask<WorkflowState> HandleAsync(
        WorkflowState input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var review = input.AiReview
            ?? throw new InvalidOperationException(
                "AiReview must be populated by AnalysisExecutor.");

        // Hard gate: low confidence always routes to human review, even if
        // the model said "approve". The threshold is configuration-driven.
        if (review.Confidence < _options.Value.HumanReviewConfidenceThreshold
            && review.Decision == "approve")
        {
            review = review with
            {
                Decision = "human_review",
                KeyRisks = review.KeyRisks
                    .Append("Confidence below configured threshold.")
                    .ToList(),
            };
        }

        // Hard gate: revision requests that have exhausted the cap must not
        // loop. Route them to human review with an explanatory note.
        if (review.Decision == "revise"
            && input.RevisionCount >= _options.Value.MaximumRevisionAttempts)
        {
            review = review with
            {
                Decision = "human_review",
                KeyRisks = review.KeyRisks
                    .Append($"Revision cap reached ({_options.Value.MaximumRevisionAttempts}).")
                    .ToList(),
            };
        }

        return ValueTask.FromResult(input with
        {
            AiReview = review,
            RevisionCount = review.Decision == "revise"
                ? input.RevisionCount + 1
                : input.RevisionCount,
            Status = "reviewed",
        });
    }
}
