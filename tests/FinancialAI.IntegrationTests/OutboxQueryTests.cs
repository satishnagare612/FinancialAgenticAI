using FinancialAI.Domain;
using FinancialAI.Infrastructure.Cosmos;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace FinancialAI.IntegrationTests;

[Collection("CosmosEmulator")]
public class OutboxQueryTests
{
    private readonly CosmosEmulatorFixture _fixture;

    public OutboxQueryTests(CosmosEmulatorFixture fixture) => _fixture = fixture;

    [RequiresEmulatorFact]
    public async Task Outbox_Document_Matches_Dispatcher_Query()
    {
        var reports = _fixture.Client.GetContainer(
            CosmosBootstrapper.DatabaseName, "reports");
        var repo = new CosmosOutboxRepository(_fixture.Client);

        var reportId = Guid.NewGuid().ToString("N");
        var outboxId = Guid.NewGuid().ToString("N");

        var report = new Report(
            reportId, "test-user", "pending",
            Array.Empty<string>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
            null, Array.Empty<string>(), Array.Empty<string>(),
            0.0, DateTimeOffset.UtcNow);

        var outbox = new OutboxMessage(
            outboxId, reportId, "test.event", "{}", DateTimeOffset.UtcNow);

        try
        {
            await repo.WriteWithReportAsync(report, outbox, default);

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = 'outbox' AND c.publishedAtUtc = null");
            using var iter = reports.GetItemQueryIterator<OutboxMessage>(query);
            var results = await iter.ReadNextAsync();

            results.Should().ContainSingle();
            results.First().Id.Should().Be(outboxId);
            results.First().Type.Should().Be("outbox");
        }
        finally
        {
            try
            {
                await reports.DeleteItemAsync<Report>(
                reportId, new PartitionKey(reportId));
            }
            catch (CosmosException) { }
            try
            {
                await reports.DeleteItemAsync<OutboxMessage>(
                outboxId, new PartitionKey(reportId));
            }
            catch (CosmosException) { }
        }
    }
}