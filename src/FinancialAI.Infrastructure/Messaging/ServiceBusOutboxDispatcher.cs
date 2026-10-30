using System.Text.Json;
using FinancialAI.Domain;
using MassTransit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Messaging;

public sealed class ServiceBusOutboxDispatcher : BackgroundService
{
    private readonly CosmosClient _cosmos;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceBusOutboxDispatcher> _logger;

    public ServiceBusOutboxDispatcher(
        CosmosClient cosmos,
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceBusOutboxDispatcher> logger)
    {
        _cosmos = cosmos;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var container = _cosmos.GetContainer("financialai", "reports");

        while (!stoppingToken.IsCancellationRequested)
        {
            // IPublishEndpoint is scoped. BackgroundService is singleton.
            // Create one scope per polling iteration and resolve the
            // endpoint from it. All publishes in one iteration share the
            // scope; disposal at the end of the iteration cleans up.
            using var scope = _scopeFactory.CreateScope();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = 'outbox' AND c.publishedAtUtc = null");

            using var iterator = container.GetItemQueryIterator<OutboxMessage>(query);
            while (iterator.HasMoreResults)
            {
                foreach (var outboxMessage in await iterator.ReadNextAsync(stoppingToken))
                {
                    try
                    {
                        object typedMessage = outboxMessage.EventType switch
                        {
                            "report.completed" => JsonSerializer
                                .Deserialize<ReportCompleted>(outboxMessage.Payload)
                                ?? throw new InvalidOperationException(
                                    $"Failed to deserialize outbox payload for message {outboxMessage.Id}"),

                            _ => throw new InvalidOperationException(
                                $"Unknown outbox event type '{outboxMessage.EventType}' " +
                                $"on message {outboxMessage.Id}"),
                        };

                        // Preserve the outbox ID as the MassTransit message ID
                        // so redelivery after a crash produces the same ID at
                        // the consumer, which ReportCompletedConsumer's
                        // idempotency store relies on.
                        var messageId = Guid.Parse(outboxMessage.Id);

                        await publishEndpoint.Publish(typedMessage, ctx =>
                        {
                            ctx.MessageId = messageId;
                        }, stoppingToken);

                        outboxMessage.PublishedAtUtc = DateTimeOffset.UtcNow;
                        await container.ReplaceItemAsync(
                            outboxMessage, outboxMessage.Id,
                            new PartitionKey(outboxMessage.ReportId),
                            cancellationToken: stoppingToken);

                        _logger.LogInformation(
                            "Outbox message {MessageId} published for report {ReportId}.",
                            outboxMessage.Id, outboxMessage.ReportId);
                    }
                    catch (Exception ex)
                    {
                        // Do NOT mark as published. The message stays in the
                        // outbox and the next 5-second tick retries it.
                        _logger.LogError(ex,
                            "Failed to publish outbox message {MessageId} for report {ReportId}.",
                            outboxMessage.Id, outboxMessage.ReportId);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}