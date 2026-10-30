using FinancialAI.Domain;
using FinancialAI.Infrastructure.Cosmos;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace FinancialAI.UnitTests;

public class MarkCompletedAsyncTests
{
    private const string Cs =
        "AccountEndpoint=https://localhost:8081/;" +
        "AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    private static CosmosClient NewClient() => new(Cs, new CosmosClientOptions
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_Calls_Do_Not_Throw_And_Produce_Completed_Status()
    {
        using var client = NewClient();
        await CosmosBootstrapper.EnsureCreatedAsync(client);
        var repo = new CosmosReportRepository(client);
        var reports = client.GetContainer(CosmosBootstrapper.DatabaseName, "reports");

        var reportId = Guid.NewGuid().ToString("N");
        var report = new Report(
            reportId, "test", "running",
            Array.Empty<string>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
            null, Array.Empty<string>(), Array.Empty<string>(),
            0.0, DateTimeOffset.UtcNow);
        await reports.UpsertItemAsync(report, new PartitionKey(reportId));

        try
        {
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => repo.MarkCompletedAsync(reportId, default));
            await Task.WhenAll(tasks);

            var final = await repo.GetAsync(reportId, default);
            Assert.NotNull(final);
            Assert.Equal(ReportStatus.Completed.ToWireString(), final!.Status);
        }
        finally
        {
            try { await reports.DeleteItemAsync<Report>(reportId, new PartitionKey(reportId)); }
            catch (CosmosException) { }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Missing_Report_Throws_InvalidOperationException()
    {
        using var client = NewClient();
        var repo = new CosmosReportRepository(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.MarkCompletedAsync(Guid.NewGuid().ToString("N"), default));
        Assert.Contains("not found", ex.Message);
    }
}
