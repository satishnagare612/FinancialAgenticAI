using FinancialAI.Application.Projections;
using FinancialAI.Domain;

namespace FinancialAI.Infrastructure.Workflow;

public static class ReportStateMapper
{
    public static Report ToPendingReport(
    Guid reportId, string userId, string correlationId, ProjectionInput input)
    {
        return new Report(
            Id: reportId.ToString("N"),
            UserId: userId,
            Status: ReportStatus.Pending.ToWireString(),
            Assumptions:
            [
                $"Initial balance: {input.InitialBalance}",
                $"Periodic contribution: {input.PeriodicContribution}",
                $"Annual return: {input.AnnualReturnRate:P2}",
                $"Annual inflation: {input.AnnualInflationRate:P2}",
                $"Years: {input.Years}",
            ],
            BaseCaseProjections: Array.Empty<decimal>(),
            ConservativeProjections: Array.Empty<decimal>(),
            Narrative: null,
            KeyRisks: Array.Empty<string>(),
            CitedSources: Array.Empty<string>(),
            ConfidenceScore: 0.0,
            GeneratedAtUtc: DateTimeOffset.UtcNow)
        {
            CorrelationId = correlationId,
        };
    }

    public static IReadOnlyList<decimal> ToNominalSeries(
        IReadOnlyList<ProjectionPoint> points)
        => points.Select(p => p.NominalBalance).ToList();
}
