using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Agents.AI.Workflows;
using Moq;
using Xunit;

namespace FinancialAI.UnitTests;

public class HumanReviewExecutorTests
{
    [Fact]
    public async Task HandleAsync_WritesReviewReason_NotError()
    {
        var executor = new HumanReviewExecutor(new StubReports(), new StubReviewTasks());

        var state = new WorkflowState(
            ReportId: Guid.NewGuid(),
            CorrelationId: "c",
            UserId: "user",
            ProjectionInput: null,
            Projections: null,
            RetrievedContext: null,
            AiReview: new AiServiceAnalyzeResponse("n", [], 0.5, [], "human_review"),
            Options: new ReportWorkflowOptions());

        var result = await executor.HandleAsync(state, Mock.Of<IWorkflowContext>(), default);

        Assert.Equal("needs_review", result.Status);
        Assert.NotNull(result.ReviewReason);
        Assert.Null(result.Error);
    }

    private sealed class StubReports : IReportRepository
    {
        public Task<Report?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<Report?>(new Report(
                id, "user", "running", Array.Empty<string>(),
                Array.Empty<decimal>(), Array.Empty<decimal>(),
                null, Array.Empty<string>(), Array.Empty<string>(),
                0.0, DateTimeOffset.UtcNow));
        public Task SaveAsync(Report r, CancellationToken ct) => Task.CompletedTask;
        public Task MarkCompletedAsync(string id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubReviewTasks : IHumanReviewTaskRepository
    {
        public Task<HumanReviewTask> SaveAsync(HumanReviewTask t, CancellationToken ct)
            => Task.FromResult(t);
        public Task<HumanReviewTask?> GetForReportAsync(string id, CancellationToken ct)
            => Task.FromResult<HumanReviewTask?>(null);
    }
}
