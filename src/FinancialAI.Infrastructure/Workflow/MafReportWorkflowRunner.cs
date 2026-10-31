using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using WorkflowType = Microsoft.Agents.AI.Workflows.Workflow;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class MafReportWorkflowRunner : IReportWorkflowRunner
{
    /// <summary>
    /// Hard ceiling on a single workflow run. Guarantees that a workflow
    /// which MAF silently hangs on (e.g. an executor HTTP call that never
    /// returns) cannot hold a worker slot indefinitely.
    /// </summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);

    private readonly WorkflowType _workflow;
    private readonly ILogger<MafReportWorkflowRunner> _logger;

    public MafReportWorkflowRunner(
        WorkflowType workflow,
        ILogger<MafReportWorkflowRunner> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }

    public async Task RunAsync(WorkflowState state, CancellationToken ct)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(RunTimeout);

        _logger.LogInformation(
            "Workflow starting. ReportId={ReportId} Timeout={Timeout}",
            state.ReportId, RunTimeout);

        try
        {
            await using Run run = await InProcessExecution.RunAsync<WorkflowState>(
                _workflow, state, cancellationToken: runCts.Token);

            foreach (WorkflowEvent evt in run.NewEvents)
            {
                if (evt is ExecutorFailedEvent failed)
                {
                    throw new InvalidOperationException(
                        $"Executor '{failed.ExecutorId}' failed: {failed.Data}");
                }
            }

            _logger.LogInformation(
                "Workflow completed. ReportId={ReportId} EventCount={Count}",
                state.ReportId, run.NewEvents.Count());
        }
        catch (OperationCanceledException)
            when (runCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // runCts fired but the outer ct did not — this is a workflow-internal
            // hang, not a shutdown. Surface it as a TimeoutException so
            // ReportWorkflowWorker's catch block marks the report failed.
            _logger.LogError(
                "Workflow exceeded {Timeout} hard timeout. ReportId={ReportId}",
                RunTimeout, state.ReportId);
            throw new TimeoutException(
                $"Workflow exceeded {RunTimeout.TotalMinutes}-minute hard timeout " +
                $"for report {state.ReportId}.");
        }
    }
}