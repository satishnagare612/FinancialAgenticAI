using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class CalculateProjectionsExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IProjectionCalculator _calculator;
    private readonly IReportRepository _reports;
    private readonly IOptions<ReportWorkflowOptions> _options;

    public CalculateProjectionsExecutor(
        IProjectionCalculator calculator,
        IReportRepository reports,
        IOptions<ReportWorkflowOptions> options)
        : base("CalculateProjections")
    {
        _calculator = calculator;
        _reports = reports;
        _options = options;
    }

    public override async ValueTask<WorkflowState> HandleAsync(
        WorkflowState input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (input.ProjectionInput is null)
        {
            throw new InvalidOperationException(
                "ProjectionInput must be populated by LoadProfileAndValidate.");
        }

        var basePoints = _calculator.Calculate(input.ProjectionInput);

        // Conservative scenario: same model with a stricter return and a
        // slightly higher inflation assumption. Adjustments come from
        // configuration, never hardcoded.
        var conservativeInput = input.ProjectionInput with
        {
            AnnualReturnRate = Math.Max(
                0.0,
                input.ProjectionInput.AnnualReturnRate
                    - _options.Value.ConservativeReturnAdjustment),
            AnnualInflationRate =
                input.ProjectionInput.AnnualInflationRate
                    + _options.Value.ConservativeInflationAdjustment,
        };
        var conservativePoints = _calculator.Calculate(conservativeInput);

        // Patch the report the POST endpoint created. GeneratedAtUtc, Id,
        // UserId and CorrelationId are copied from the existing document
        // and never rewritten here — only the fields this step owns change.
        // Recreating the report via ToPendingReport used to reset
        // GeneratedAtUtc on every workflow run, which made the API's
        // "created" timestamp drift away from the original POST.
        var existing = await _reports.GetAsync(
            input.ReportId.ToString("N"), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Report {input.ReportId} disappeared before projection calculation.");

        var report = existing with
        {
            Status = ReportStatus.Running.ToWireString(),
            Assumptions = ReportStateMapper.BuildAssumptions(input.ProjectionInput),
            BaseCaseProjections = ReportStateMapper.ToNominalSeries(basePoints),
            ConservativeProjections = ReportStateMapper.ToNominalSeries(conservativePoints),
        };

        await _reports.SaveAsync(report, cancellationToken);

        return input with
        {
            Projections = basePoints,
            Status = "projections_calculated",
        };
    }
}