using System.ComponentModel.DataAnnotations;

namespace FinancialAI.Application.Workflow;

public sealed class ReportWorkflowOptions
{
    public const string SectionName = "ReportWorkflow";

    [Range(0.0, 1.0)]
    public double HumanReviewConfidenceThreshold { get; init; } = 0.7;

    [Range(0, 10)]
    public int MaximumRevisionAttempts { get; init; } = 2;

    /// <summary>
    /// Amount subtracted from AnnualReturnRate for the conservative
    /// scenario. Result is floored at 0. Reviewer-adjustable without a code
    /// change.
    /// </summary>
    [Range(0.0, 1.0)]
    public double ConservativeReturnAdjustment { get; init; } = 0.02;

    /// <summary>
    /// Amount added to AnnualInflationRate for the conservative scenario.
    /// </summary>
    [Range(0.0, 0.10)]
    public double ConservativeInflationAdjustment { get; init; } = 0.01;
}
