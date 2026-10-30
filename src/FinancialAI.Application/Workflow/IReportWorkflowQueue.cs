namespace FinancialAI.Application.Workflow;

/// <summary>
/// In-process queue for reports awaiting workflow execution.
/// Issue 2 will replace this with an outbox-backed Service Bus publish;
/// the port shape stays unchanged so callers are unaffected.
/// </summary>
public interface IReportWorkflowQueue
{
    /// <summary>
    /// Enqueues a workflow state for background execution. Returns as soon
    /// as the item is accepted; the workflow itself runs on a hosted service.
    /// </summary>
    ValueTask EnqueueAsync(WorkflowState state, CancellationToken ct);

    /// <summary>Consumed by the worker; not for direct use by endpoints.</summary>
    IAsyncEnumerable<WorkflowState> ReadAllAsync(CancellationToken ct);
}