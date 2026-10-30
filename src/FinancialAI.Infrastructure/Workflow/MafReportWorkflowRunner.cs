using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;
using WorkflowType = Microsoft.Agents.AI.Workflows.Workflow;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class MafReportWorkflowRunner : IReportWorkflowRunner
{
    private readonly WorkflowType _workflow;

    public MafReportWorkflowRunner(WorkflowType workflow) => _workflow = workflow;

    public async Task RunAsync(WorkflowState state, CancellationToken ct)
    {
        // Workflow has no RunAsync. Execution is a static concern in
        // InProcessExecution. The generic parameter is the workflow's
        // input type; WorkflowState is what every executor accepts/returns.
        // Run implements IAsyncDisposable — always dispose it.
        await using Run run = await InProcessExecution.RunAsync<WorkflowState>(
            _workflow, state, cancellationToken: ct);

        // Optional: inspect events for observability. The workflow itself
        // already persisted state through the executors' repository calls,
        // so we do not need to read the final WorkflowState from the events.
        foreach (WorkflowEvent evt in run.NewEvents)
        {
            if (evt is ExecutorFailedEvent failed)
            {
                throw new InvalidOperationException(
                    $"Executor '{failed.ExecutorId}' failed: {failed.Data}");
            }
        }
    }
}