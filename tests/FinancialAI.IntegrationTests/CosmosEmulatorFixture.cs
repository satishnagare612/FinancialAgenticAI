using FinancialAI.Infrastructure.Cosmos;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace FinancialAI.IntegrationTests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    public const string ConnectionString =
        "AccountEndpoint=https://localhost:8081/;" +
        "AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    public CosmosClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = new CosmosClient(ConnectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
            }),
        });

        // Idempotent — creates DB and all containers if absent.
        await CosmosBootstrapper.EnsureCreatedAsync(Client);
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("CosmosEmulator")]
public sealed class CosmosEmulatorCollection
    : ICollectionFixture<CosmosEmulatorFixture>
{ }
