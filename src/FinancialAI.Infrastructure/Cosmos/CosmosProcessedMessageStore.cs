using System.Net;
using FinancialAI.Application.Ports;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

/// <summary>
/// Idempotency ledger for MassTransit consumers. Uses the processed-messages
/// container (created by <see cref="CosmosBootstrapper"/>) with a 24-hour TTL,
/// which is longer than any plausible Service Bus redelivery window.
/// </summary>
public sealed class CosmosProcessedMessageStore : IProcessedMessageStore
{
    private readonly Container _container;

    public CosmosProcessedMessageStore(CosmosClient client)
        => _container = client.GetContainer(
            CosmosBootstrapper.DatabaseName, "processed-messages");

    public async Task<bool> ExistsAsync(string messageId, CancellationToken ct)
    {
        try
        {
            await _container.ReadItemAsync<ProcessedMessage>(
                messageId, new PartitionKey(messageId), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task RecordAsync(string messageId, CancellationToken ct)
    {
        try
        {
            await _container.CreateItemAsync(
                new ProcessedMessage(messageId, DateTimeOffset.UtcNow),
                new PartitionKey(messageId),
                cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Two consumers raced on the same message. The work is already
            // recorded; treat as success. The consumer's job was idempotent
            // by design (MarkCompletedAsync is an upsert), so the lost race
            // produced no side effect.
        }
    }

    private sealed record ProcessedMessage(
        string Id, DateTimeOffset RecordedAtUtc);
}
