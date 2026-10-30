using FluentAssertions;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Agents.AI.Workflows;
using Moq;
using Xunit;

namespace FinancialAI.UnitTests;

public class LoadProfileAndValidateExecutorTests
{
    [Fact]
    public async Task HandleAsync_UsesCallerProjectionYears()
    {
        var profiles = new StubProfileRepository();
        var executor = new LoadProfileAndValidateExecutor(profiles);
        var state = NewState() with { ProjectionYears = 37 };

        var result = await executor.HandleAsync(
            state, Mock.Of<IWorkflowContext>(), CancellationToken.None);

        result.ProjectionInput.Should().NotBeNull();
        result.ProjectionInput!.Years.Should().Be(37,
            "the caller's requested horizon must be used, not a derived value");
    }

    [Fact]
    public async Task HandleAsync_UsesDefaultProjectionYearsWhenNotSet()
    {
        // Demonstrates that the init default of 20 is a fallback, not a
        // silent behaviour that matches the old bug. When the endpoint
        // does not set ProjectionYears (only test code), the default
        // applies and the workflow still runs.
        var profiles = new StubProfileRepository();
        var executor = new LoadProfileAndValidateExecutor(profiles);
        var state = NewState();

        var result = await executor.HandleAsync(
            state, Mock.Of<IWorkflowContext>(), CancellationToken.None);

        result.ProjectionInput.Should().NotBeNull();
        result.ProjectionInput!.Years.Should().Be(20);
    }

    private static WorkflowState NewState() => new(
        ReportId: Guid.NewGuid(),
        CorrelationId: "corr",
        UserId: "user",
        ProjectionInput: null,
        Projections: null,
        RetrievedContext: null,
        AiReview: null,
        Options: new ReportWorkflowOptions());

    private sealed class StubProfileRepository : IFinancialProfileRepository
    {
        private readonly FinancialProfile _profile = new(
            Id: "user", UserId: "user",
            InitialBalance: 10_000m, MonthlyContribution: 100m,
            ExpectedReturnRate: 0.06, InflationRate: 0.02,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        public Task<FinancialProfile?> GetByUserIdAsync(string userId, CancellationToken ct)
            => Task.FromResult<FinancialProfile?>(_profile);

        public Task<FinancialProfile> UpsertAsync(FinancialProfile profile, CancellationToken ct)
            => Task.FromResult(profile);

        public Task<bool> DeleteAsync(string userId, CancellationToken ct)
            => Task.FromResult(true);
    }
}
