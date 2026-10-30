using FinancialAI.Application.Contracts;
using FinancialAI.Domain;

namespace FinancialAI.Application.Ports;

public interface IFinancialProfileRepository
{
    Task<FinancialProfile?> GetByUserIdAsync(string userId, CancellationToken ct);
    Task<FinancialProfile> UpsertAsync(FinancialProfile profile, CancellationToken ct);
    Task<bool> DeleteAsync(string userId, CancellationToken ct);
}

public interface IReportRepository
{
    Task<Report?> GetAsync(string reportId, CancellationToken ct);
    Task SaveAsync(Report report, CancellationToken ct);
    Task MarkCompletedAsync(string reportId, CancellationToken ct);
}

public interface IOutboxRepository
{
    Task WriteWithReportAsync(Report report, OutboxMessage message, CancellationToken ct);
}

public interface IReportRenderer
{
    byte[] Render(Report report);
}

public interface IAiServiceClient
{
    Task<AiServiceAnalyzeResponse> AnalyzeAsync(
        AiServiceAnalyzeRequest request, CancellationToken ct);
}
public interface IProcessedMessageStore
{
    Task<bool> ExistsAsync(string messageId, CancellationToken ct);
    Task RecordAsync(string messageId, CancellationToken ct);
}

public interface IHumanReviewTaskRepository
{
    /// <summary>
    /// Upsert a review task. Using the same task id twice is idempotent,
    /// which matters when the workflow retries a human-review step.
    /// </summary>
    Task<HumanReviewTask> SaveAsync(HumanReviewTask task, CancellationToken ct);

    /// <summary>
    /// The current review task for the given report, or null if none exists.
    /// There is at most one open task per report at a time.
    /// </summary>
    Task<HumanReviewTask?> GetForReportAsync(string reportId, CancellationToken ct);
}
