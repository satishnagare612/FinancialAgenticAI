using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinancialAI.UnitTests;

public class CalculateProjectionsExecutorTests
{
    [Fact]
    public async Task HandleAsync_UsesConfiguredAdjustments()
    {
        var options = Options.Create(new ReportWorkflowOptions
        {
            ConservativeReturnAdjustment = 0.05,
            ConservativeInflationAdjustment = 0.03,
        });
        var executor = new CalculateProjectionsExecutor(
            new ProjectionCalculator(), new StubReports(), options);

        var state = new WorkflowState(
            ReportId: Guid.NewGuid(),
            CorrelationId: "c",
            UserId: "user",
            ProjectionInput: new ProjectionInput(10000m, 100m, 0.08, 0.02, 5),
            Projections: null,
            RetrievedContext: null,
            AiReview: null,
            Options: options.Value);

        await executor.HandleAsync(state, Mock.Of<IWorkflowContext>(), default);

        // The conservative projections must use rate 0.08-0.05=0.03 and
        // inflation 0.02+0.03=0.05, not the old hardcoded 0.06 and 0.03.
        // Asserting via the persisted report's ConservativeProjections[^1]
        // versus a calculator run with the explicitly-adjusted input.
        var expectedInput = state.ProjectionInput! with
        {
            AnnualReturnRate = 0.03,
            AnnualInflationRate = 0.05,
        };
        var expected = new ProjectionCalculator().Calculate(expectedInput);

        var saved = StubReports.LastSaved!;
        Assert.Equal(expected[^1].NominalBalance, saved.ConservativeProjections[^1]);
    }

    private sealed class StubReports : IReportRepository
    {
        public static Report? LastSaved { get; private set; }

        public Task<Report?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<Report?>(null);
        public Task SaveAsync(Report r, CancellationToken ct)
        {
            LastSaved = r;
            return Task.CompletedTask;
        }
        public Task MarkCompletedAsync(string id, CancellationToken ct) => Task.CompletedTask;
    }
}

