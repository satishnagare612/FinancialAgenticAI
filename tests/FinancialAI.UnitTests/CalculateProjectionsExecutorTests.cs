using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;
using Moq;

namespace FinancialAI.UnitTests;

/// <summary>
/// Tests for <see cref="CalculateProjectionsExecutor"/>.
///
/// Since the F-002 fix, the executor patches the report the POST endpoint
/// created (via <see cref="IReportRepository.GetAsync"/>) instead of
/// recreating it. Every test that expects a successful run must therefore
/// seed <c>GetAsync</c> with an existing report — see
/// <see cref="SeedRepo"/>. Tests that skip the seed hit the "disappeared
/// before projection calculation" guard and must assert on the throw.
/// </summary>
public class CalculateProjectionsExecutorTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Mock seeder — every happy-path test goes through this.
    // Default <c>GeneratedAtUtc</c> is in the past so the F-002 invariant
    // (timestamp preserved across save) is observable.
    // ─────────────────────────────────────────────────────────────────────
    private static Mock<IReportRepository> SeedRepo(
        Guid reportId,
        Report? existing = null,
        string userId = "test-user",
        string correlationId = "test-correlation")
    {
        var seeded = existing ?? new Report(
            Id: reportId.ToString("N"),
            UserId: userId,
            Status: ReportStatus.Pending.ToWireString(),
            Assumptions: Array.Empty<string>(),
            BaseCaseProjections: Array.Empty<decimal>(),
            ConservativeProjections: Array.Empty<decimal>(),
            Narrative: null,
            KeyRisks: Array.Empty<string>(),
            CitedSources: Array.Empty<string>(),
            ConfidenceScore: 0.0,
            GeneratedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            CorrelationId = correlationId,
        };

        var repo = new Mock<IReportRepository>();
        repo.Setup(r => r.GetAsync(
                reportId.ToString("N"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(seeded);
        repo.Setup(r => r.SaveAsync(
                It.IsAny<Report>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static Report GetSavedReport(Mock<IReportRepository> repo)
    {
        var saves = repo.Invocations
            .Where(i => i.Method.Name == nameof(IReportRepository.SaveAsync))
            .Select(i => (Report)i.Arguments[0]!)
            .ToList();

        Assert.Single(saves);
        return saves[0];
    }

    // ─────────────────────────────────────────────────────────────────────
    // The originally-failing test — verifies configured (non-default)
    // adjustments reach the saved report AND the F-002 invariant holds.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_UsesConfiguredAdjustments()
    {
        // ── Arrange ──────────────────────────────────────────────────────
        // Non-default adjustments so a regression to the defaults
        // (0.02 / 0.01) is caught — "UsesConfiguredAdjustments" is
        // exactly the property under test.
        const double returnAdj = 0.05;
        const double inflationAdj = 0.03;

        var options = Options.Create(new ReportWorkflowOptions
        {
            ConservativeReturnAdjustment = returnAdj,
            ConservativeInflationAdjustment = inflationAdj,
        });

        var projectionInput = new ProjectionInput(
            InitialBalance: 100_000m,
            PeriodicContribution: 500m,
            AnnualReturnRate: 0.08,
            AnnualInflationRate: 0.02,
            Years: 5);

        var reportId = Guid.NewGuid();
        var repo = SeedRepo(reportId);
        var calculator = new ProjectionCalculator();
        var executor = new CalculateProjectionsExecutor(
            calculator, repo.Object, options);

        var state = new WorkflowState(
            ReportId: reportId,
            CorrelationId: "test-correlation",
            UserId: "test-user",
            ProjectionInput: projectionInput,
            Projections: null,
            RetrievedContext: null,
            AiReview: null,
            Options: options.Value);

        // ── Act ──────────────────────────────────────────────────────────
        var result = await executor.HandleAsync(
            state,
            Mock.Of<IWorkflowContext>(),
            CancellationToken.None);

        // ── Assert ───────────────────────────────────────────────────────
        // 1. Base case uses the UNadjusted input.
        var expectedBase = calculator.Calculate(projectionInput)
            .Select(p => p.NominalBalance)
            .ToList();

        // 2. Conservative case applies the configured adjustments.
        var expectedConservativeInput = projectionInput with
        {
            AnnualReturnRate = Math.Max(
                0.0, projectionInput.AnnualReturnRate - returnAdj),
            AnnualInflationRate = projectionInput.AnnualInflationRate + inflationAdj,
        };
        var expectedConservative = calculator.Calculate(expectedConservativeInput)
            .Select(p => p.NominalBalance)
            .ToList();

        var saved = GetSavedReport(repo);

        Assert.Equal(expectedBase, saved.BaseCaseProjections);
        Assert.Equal(expectedConservative, saved.ConservativeProjections);

        // 3. F-002 regression guard: the executor MUST preserve the
        //    creation timestamp. Reverting to ToPendingReport-style
        //    recreation would reset it to UtcNow and fail this assertion.
        var existing = await repo.Object.GetAsync(
            reportId.ToString("N"), CancellationToken.None);
        Assert.NotNull(existing);
        Assert.Equal(existing!.GeneratedAtUtc, saved.GeneratedAtUtc);

        // 4. Status transitioned to running (not pending, not completed).
        Assert.Equal(ReportStatus.Running.ToWireString(), saved.Status);

        // 5. State returned to the caller carries the base-case points.
        Assert.NotNull(result.Projections);
        Assert.Equal(projectionInput.Years + 1, result.Projections!.Count);
        Assert.Equal("projections_calculated", result.Status);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Guard: the report must exist. If the POST record disappears before
    // the workflow reaches this step, we want a clear failure — not a
    // silent re-creation with a fresh timestamp (the pre-F-002 behavior).
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_ThrowsWhenReportIsMissing()
    {
        var reportId = Guid.NewGuid();
        var repo = new Mock<IReportRepository>();
        repo.Setup(r => r.GetAsync(
                reportId.ToString("N"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Report?)null);

        var options = Options.Create(new ReportWorkflowOptions());
        var executor = new CalculateProjectionsExecutor(
            new ProjectionCalculator(), repo.Object, options);

        var state = new WorkflowState(
            ReportId: reportId,
            CorrelationId: "corr",
            UserId: "user",
            ProjectionInput: new ProjectionInput(100m, 0m, 0.05, 0.02, 1),
            Projections: null,
            RetrievedContext: null,
            AiReview: null,
            Options: options.Value);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.HandleAsync(
                state,
                Mock.Of<IWorkflowContext>(),
                CancellationToken.None));

        Assert.Contains("disappeared before projection calculation", ex.Message);

        // Critically: the executor must NOT have called SaveAsync when the
        // report was missing. Otherwise a downstream step would see a
        // half-written document.
        repo.Verify(
            r => r.SaveAsync(It.IsAny<Report>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Guard: caller must supply projections before this step runs.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_ThrowsWhenProjectionInputIsNull()
    {
        var reportId = Guid.NewGuid();
        var repo = SeedRepo(reportId);
        var options = Options.Create(new ReportWorkflowOptions());
        var executor = new CalculateProjectionsExecutor(
            new ProjectionCalculator(), repo.Object, options);

        var state = new WorkflowState(
            ReportId: reportId,
            CorrelationId: "corr",
            UserId: "user",
            ProjectionInput: null,                  // ← the missing input
            Projections: null,
            RetrievedContext: null,
            AiReview: null,
            Options: options.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.HandleAsync(
                state,
                Mock.Of<IWorkflowContext>(),
                CancellationToken.None));
    }
}