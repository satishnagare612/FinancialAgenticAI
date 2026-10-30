using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

public sealed class CosmosFinancialProfileRepository : IFinancialProfileRepository
{
    private readonly Container _container;

    public CosmosFinancialProfileRepository(CosmosClient client)
        => _container = client.GetContainer("financialai", "profiles");

    public async Task<FinancialProfile?> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        // Id == UserId convention, so a point read is sufficient — no query needed.
        try
        {
            var response = await _container.ReadItemAsync<FinancialProfile>(
                userId, new PartitionKey(userId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<FinancialProfile> UpsertAsync(
        FinancialProfile profile, CancellationToken ct)
    {
        // Id must equal UserId to make the point read above valid.
        if (!string.Equals(profile.Id, profile.UserId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "FinancialProfile.Id must equal UserId for the current schema.");
        }

        var response = await _container.UpsertItemAsync(
            profile, new PartitionKey(profile.UserId), cancellationToken: ct);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string userId, CancellationToken ct)
    {
        try
        {
            await _container.DeleteItemAsync<FinancialProfile>(
                userId, new PartitionKey(userId), cancellationToken: ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

public sealed class CosmosReportRepository : IReportRepository
{
    private readonly Container _container;

    public CosmosReportRepository(CosmosClient client)
        => _container = client.GetContainer("financialai", "reports");

    public async Task<Report?> GetAsync(string reportId, CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<Report>(
                reportId, new PartitionKey(reportId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(Report report, CancellationToken ct)
        => await _container.UpsertItemAsync(report, new PartitionKey(report.Id), cancellationToken: ct);

    public async Task MarkCompletedAsync(string reportId, CancellationToken ct)
    {
        // Guard: ReportStatus wire values must match the patched literal.
        if (ReportStatus.Completed.ToWireString() != "completed")
        {
            throw new InvalidOperationException(
                $"ReportStatus.Completed.ToWireString() is " +
                $"'{ReportStatus.Completed.ToWireString()}', but MarkCompletedAsync " +
                "patches the literal \"completed\". Update the PatchOperation to " +
                "use the current wire value.");
        }

        try
        {
            await _container.PatchItemAsync<Report>(
                reportId,
                new PartitionKey(reportId),
                new[] { PatchOperation.Set("/status", "completed") },
                cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Report {reportId} not found.", ex);
        }
    }
}
