using FluentAssertions;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Workflow;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Extensions.Options;
using Xunit;

public class WorkflowRoutingTests
{
    private static IOptions<ReportWorkflowOptions> Options(double threshold = 0.7)
        => Microsoft.Extensions.Options.Options.Create(
            new ReportWorkflowOptions
            {
                HumanReviewConfidenceThreshold = threshold,
                MaximumRevisionAttempts = 2,
            });

    [Fact]
    public async Task LowConfidence_RoutesToHumanReview()
    {
        var executor = new ReviewExecutor(Options());
        var state = new WorkflowState(
            Guid.NewGuid(), "corr", "user", null, null, null,
            new AiServiceAnalyzeResponse("n", [], 0.5, [], "approve"),
            Options().Value);

        var result = await executor.HandleAsync(state, default);
        result.AiReview!.Decision.Should().Be("human_review");
    }
}
