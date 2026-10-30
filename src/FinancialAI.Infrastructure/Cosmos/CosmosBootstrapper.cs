using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Cosmos;

/// <summary>
/// Creates the 'financialai' database and its containers if they do not exist.
/// Intended for local development and integration tests. Production should
/// provision containers via Bicep (see blueprint §23) so the API does not
/// need control-plane permissions.
/// </summary>
public static class CosmosBootstrapper
{
    public const string DatabaseName = "financialai";

    public static async Task EnsureCreatedAsync(
        CosmosClient client, CancellationToken ct = default)
    {
        var database = await client.CreateDatabaseIfNotExistsAsync(
            DatabaseName, cancellationToken: ct);

        // profiles: partition key is /userId (Id == UserId by app convention)
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("profiles", "/userId"),
            cancellationToken: ct);

        // reports: Report and OutboxMessage share the 'reportId' field,
        // enabling a transactional batch (see CosmosOutboxRepository).
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("reports", "/reportId"),
            cancellationToken: ct);

        // sagas: MassTransit Cosmos saga persistence (correlation id is 'id')
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("sagas", "/id"),
            cancellationToken: ct);

        // processed-messages: idempotent-consumer ledger (Issue 8); TTL = 24h
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("processed-messages", "/id")
            {
                DefaultTimeToLive = 86400,
            },
            cancellationToken: ct);

        // human-reviews: partition by /reportId so GetForReportAsync is single-partition.
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("human-reviews", "/reportId"),
            cancellationToken: ct);
    }
}

/// <summary>
/// Runs the bootstrapper once at host startup, before any other hosted service
/// that touches Cosmos. Requires control-plane permission on the account;
/// disable in production by removing this registration.
/// </summary>
public sealed class CosmosBootstrapperService : IHostedService
{
    private readonly CosmosClient _client;
    private readonly ILogger<CosmosBootstrapperService> _logger;

    public CosmosBootstrapperService(
        CosmosClient client, ILogger<CosmosBootstrapperService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring Cosmos database and containers exist.");
        await CosmosBootstrapper.EnsureCreatedAsync(_client, cancellationToken);
        _logger.LogInformation("Cosmos bootstrap complete.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}