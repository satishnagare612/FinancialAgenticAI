using FinancialAI.Domain;
using FinancialAI.Infrastructure.Cosmos;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace FinancialAI.IntegrationTests;

[Collection("CosmosEmulator")]
public class HumanReviewTaskRepositoryTests
{
    private readonly CosmosEmulatorFixture _fixture;

    public HumanReviewTaskRepositoryTests(CosmosEmulatorFixture fixture)
        => _fixture = fixture;

    [RequiresEmulatorFact]
    public async Task Save_Then_Get_RoundTrips()
    {
        var repo = new CosmosHumanReviewTaskRepository(_fixture.Client);
        var container = _fixture.Client.GetContainer(
            CosmosBootstrapper.DatabaseName, "human-reviews");

        var reportId = Guid.NewGuid().ToString("N");
        var task = new HumanReviewTask(
            Guid.NewGuid().ToString("N"), reportId,
            "test reason", "queued", DateTimeOffset.UtcNow);

        try
        {
            await repo.SaveAsync(task, default);
            var loaded = await repo.GetForReportAsync(reportId, default);
            Assert.NotNull(loaded);
            Assert.Equal(task.Id, loaded!.Id);
            Assert.Equal(reportId, loaded.ReportId);
        }
        finally
        {
            try
            {
                await container.DeleteItemAsync<HumanReviewTask>(
                task.Id, new PartitionKey(reportId));
            }
            catch (CosmosException) { }
        }
    }
}