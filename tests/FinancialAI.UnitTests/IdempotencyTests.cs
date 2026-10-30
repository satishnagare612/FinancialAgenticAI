using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using FinancialAI.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinancialAI.UnitTests;

public class IdempotencyTests
{
    [Fact]
    public async Task SameMessageId_Twice_ProducesOneSideEffect()
    {
        var store = new InMemoryProcessedMessageStore();
        var reports = new RecordingReportRepository();
        var consumer = new ReportCompletedConsumer(
            store, reports, NullLogger<ReportCompletedConsumer>.Instance);

        var reportId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await consumer.Consume(ContextFor(messageId, reportId));
        await consumer.Consume(ContextFor(messageId, reportId));

        Assert.Single(reports.Completed);
        Assert.Equal(reportId.ToString("N"), reports.Completed[0]);
    }

    [Fact]
    public async Task DifferentMessageIds_ProduceTwoSideEffects()
    {
        var store = new InMemoryProcessedMessageStore();
        var reports = new RecordingReportRepository();
        var consumer = new ReportCompletedConsumer(
            store, reports, NullLogger<ReportCompletedConsumer>.Instance);

        await consumer.Consume(ContextFor(Guid.NewGuid(), Guid.NewGuid()));
        await consumer.Consume(ContextFor(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(2, reports.Completed.Count);
    }

    private static ConsumeContext<ReportCompleted> ContextFor(
        Guid messageId, Guid reportId)
    {
        var ctx = new Mock<ConsumeContext<ReportCompleted>>();
        ctx.SetupGet(c => c.MessageId).Returns(messageId);
        ctx.SetupGet(c => c.Message).Returns(new ReportCompleted(reportId));
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        ctx.SetupGet(c => c.CorrelationId).Returns(Guid.NewGuid());
        return ctx.Object;
    }

    private sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
    {
        private readonly HashSet<string> _seen = new();
        public Task<bool> ExistsAsync(string messageId, CancellationToken ct)
            => Task.FromResult(_seen.Contains(messageId));
        public Task RecordAsync(string messageId, CancellationToken ct)
        {
            _seen.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReportRepository : IReportRepository
    {
        public List<string> Completed { get; } = new();
        public Task<Report?> GetAsync(string reportId, CancellationToken ct)
            => Task.FromResult<Report?>(null);
        public Task SaveAsync(Report report, CancellationToken ct)
            => Task.CompletedTask;
        public Task MarkCompletedAsync(string reportId, CancellationToken ct)
        {
            Completed.Add(reportId);
            return Task.CompletedTask;
        }
    }
}
