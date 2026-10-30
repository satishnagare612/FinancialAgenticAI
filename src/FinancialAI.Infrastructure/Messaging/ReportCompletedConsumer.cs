using FinancialAI.Application.Ports;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Messaging;

public sealed partial class ReportCompletedConsumer : IConsumer<ReportCompleted>
{
    private readonly IProcessedMessageStore _store;
    private readonly IReportRepository _reports;
    private readonly ILogger<ReportCompletedConsumer> _logger;

    public ReportCompletedConsumer(
        IProcessedMessageStore store,
        IReportRepository reports,
        ILogger<ReportCompletedConsumer> logger)
    {
        _store = store;
        _reports = reports;
        _logger = logger;
    }

    /// <summary>
    /// Idempotent consumer for <see cref="ReportCompleted"/>.
    /// </summary>
    /// <remarks>
    /// Service Bus delivers at-least-once, so this method may be called more
    /// than once for the same message. Safety relies on two invariants:
    /// <list type="bullet">
    ///   <item><see cref="IReportRepository.MarkCompletedAsync"/> MUST be an
    ///   upsert (set status = completed), not an insert.</item>
    ///   <item><see cref="IProcessedMessageStore.RecordAsync"/> MUST be an
    ///   upsert keyed by messageId.</item>
    /// </list>
    /// If <see cref="MarkCompletedAsync"/> ever changes to insert-only, replace
    /// this method's <c>ExistsAsync</c> check with an atomic
    /// <c>TryRecordAsync</c> on the store and record BEFORE doing the work.
    /// </remarks>
    public async Task Consume(ConsumeContext<ReportCompleted> context)
    {
        var messageId = context.MessageId
            ?? throw new InvalidOperationException(
                $"ReportCompleted message for report '{context.Message.ReportId}' " +
                "is missing the Service Bus MessageId required for idempotent processing.");

        // ConsumeContext.MessageId is Guid?; IProcessedMessageStore keys on string.
        // Convert once and reuse, so the log properties and the store calls
        // cannot drift out of sync.
        var messageIdString = messageId.ToString("N");
        var reportId = context.Message.ReportId.ToString("N");

        using var _ = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = messageIdString,
            ["ReportId"] = reportId,
            ["CorrelationId"] = context.CorrelationId,
        });

        if (await _store.ExistsAsync(messageIdString, context.CancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Duplicate delivery of message {MessageId} for report {ReportId}; skipping.",
                messageIdString, reportId);
            return;
        }

        await _reports
            .MarkCompletedAsync(reportId, context.CancellationToken)
            .ConfigureAwait(false);

        await _store
            .RecordAsync(messageIdString, context.CancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Processed message {MessageId}; report {ReportId} marked completed.",
            messageIdString, reportId);
    }
}