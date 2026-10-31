using FinancialAI.Application.Projections;
using FinancialAI.Domain;

namespace FinancialAI.Infrastructure.Workflow;

public static class ReportStateMapper
{
    /// <summary>
    /// Assumption strings surfaced on the report detail response and PDF.
    /// Shared between report creation (<c>ReportEndpoints.MapPost</c>) and
    /// projection calculation (<c>CalculateProjectionsExecutor</c>) so the
    /// two paths cannot drift.
    /// </summary>
    public static IReadOnlyList<string> BuildAssumptions(ProjectionInput input)
        => new[]
        {
            $"Initial balance: {input.InitialBalance}",
            $"Periodic contribution: {input.PeriodicContribution}",
            $"Annual return: {input.AnnualReturnRate:P2}",
            $"Annual inflation: {input.AnnualInflationRate:P2}",
            $"Years: {input.Years}",
        };

    public static IReadOnlyList<decimal> ToNominalSeries(
        IReadOnlyList<ProjectionPoint> points)
        => points.Select(p => p.NominalBalance).ToList();
}