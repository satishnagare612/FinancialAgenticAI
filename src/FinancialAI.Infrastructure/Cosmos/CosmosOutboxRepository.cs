using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

public sealed class CosmosOutboxRepository : IOutboxRepository
{
    private readonly Container _reportsContainer;

    public CosmosOutboxRepository(CosmosClient client)
    {
        // Outbox documents live in the same container as reports so a single
        // transactional batch (same partition key = reportId) can write both.
        _reportsContainer = client.GetContainer("financialai", "reports");
    }

    public async Task WriteWithReportAsync(
        Report report, OutboxMessage message, CancellationToken ct)
    {
        var batch = _reportsContainer.CreateTransactionalBatch(new PartitionKey(report.Id));
        batch.UpsertItem(report);
        batch.CreateItem(message);

        using var response = await batch.ExecuteAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Outbox batch failed: {response.StatusCode} - {response.ErrorMessage}");
        }
    }
}
