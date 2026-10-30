using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace FinancialAI.Infrastructure.Workflow;

public sealed partial class LoadProfileAndValidateExecutor
    : Executor<WorkflowState, WorkflowState>
{
    private readonly IFinancialProfileRepository _profiles;

    public LoadProfileAndValidateExecutor(IFinancialProfileRepository profiles)
        : base("LoadProfileAndValidate")
    {
        _profiles = profiles;
    }

    public override async ValueTask<WorkflowState> HandleAsync(
    WorkflowState input,
    IWorkflowContext context,
    CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetByUserIdAsync(input.UserId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No financial profile found for user '{input.UserId}'.");

        var projectionInput = new ProjectionInput(
            InitialBalance: profile.InitialBalance,
            PeriodicContribution: profile.MonthlyContribution,
            AnnualReturnRate: profile.ExpectedReturnRate,
            AnnualInflationRate: profile.InflationRate,
            Years: input.ProjectionYears);

        ProjectionValidator.Validate(projectionInput);

        return input with
        {
            ProjectionInput = projectionInput,
            Status = "profile_loaded",
        };
    }
}