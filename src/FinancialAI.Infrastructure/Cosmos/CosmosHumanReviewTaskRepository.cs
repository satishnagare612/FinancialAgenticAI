using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

/// <summary>
/// Cosmos-backed storage for human-review tasks. Partition key is /reportId,
/// so lookups by report are single-partition (fast) and a report's task is
/// co-located with the report's other artifacts by partition identity.
/// </summary>
public sealed class CosmosHumanReviewTaskRepository : IHumanReviewTaskRepository
{
    private readonly Container _container;

    public CosmosHumanReviewTaskRepository(CosmosClient client)
        => _container = client.GetContainer(
            CosmosBootstrapper.DatabaseName, "human-reviews");

    public async Task<HumanReviewTask> SaveAsync(
        HumanReviewTask task, CancellationToken ct)
    {
        var response = await _container.UpsertItemAsync(
            task, new PartitionKey(task.ReportId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<HumanReviewTask?> GetForReportAsync(
        string reportId, CancellationToken ct)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.reportId = @reportId")
            .WithParameter("@reportId", reportId);

        using var iterator = _container.GetItemQueryIterator<HumanReviewTask>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(reportId),
                MaxItemCount = 1,
            });

        var response = await iterator.ReadNextAsync(ct);
        return response.FirstOrDefault();
    }
}
