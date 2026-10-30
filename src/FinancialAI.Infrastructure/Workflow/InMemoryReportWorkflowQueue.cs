using System.Threading.Channels;

using FinancialAI.Application.Workflow;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class InMemoryReportWorkflowQueue : IReportWorkflowQueue
{
    private readonly Channel<WorkflowState> _channel =
        Channel.CreateBounded<WorkflowState>(
            new BoundedChannelOptions(capacity: 100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });

    public ValueTask EnqueueAsync(WorkflowState state, CancellationToken ct)
        => _channel.Writer.WriteAsync(state, ct);

    public IAsyncEnumerable<WorkflowState> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
