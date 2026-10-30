using FinancialAI.Application.Workflow;

namespace FinancialAI.Application.Ports;

public interface IReportWorkflowRunner
{
    Task RunAsync(WorkflowState state, CancellationToken ct);
}