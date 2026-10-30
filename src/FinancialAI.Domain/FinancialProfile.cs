namespace FinancialAI.Domain;

public sealed record FinancialProfile(
    string Id,
    string UserId,
    decimal InitialBalance,
    decimal MonthlyContribution,
    double ExpectedReturnRate,
    double InflationRate,
    DateTimeOffset CreatedAtUtc);

public sealed record Report(
    string Id,
    string UserId,
    string Status,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<decimal> BaseCaseProjections,
    IReadOnlyList<decimal> ConservativeProjections,
    string? Narrative,
    IReadOnlyList<string> KeyRisks,
    IReadOnlyList<string> CitedSources,
    double ConfidenceScore,
    DateTimeOffset GeneratedAtUtc)
{
    /// <summary>Discriminator for the shared 'reports' container.</summary>
    public string Type { get; init; } = "report";

    /// <summary>Shared partition key with OutboxMessage.</summary>
    public string ReportId => Id;

    /// <summary>
    /// Correlation ID for the request that created this report. Set once
    /// at creation in ReportEndpoints.MapPost and never changed. Populated
    /// on ReportStateDto so the client can correlate a report with
    /// server-side traces without needing to know the workflow's internal
    /// state. Default "" so reports written before this field existed
    /// deserialize without error — they simply have no correlation ID.
    /// </summary>
    public string CorrelationId { get; init; } = "";
}


public sealed record OutboxMessage(
    string Id,
    string ReportId,
    string EventType,
    string Payload,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>
    /// Discriminator for the shared 'reports' container. The outbox
    /// dispatcher's polling query matches <c>WHERE c.type = 'outbox'</c>.
    /// </summary>
    public string Type { get; init; } = "outbox";

    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public enum ReportStatus
{
    Pending,
    Running,
    NeedsReview,
    Completed,
    Failed,
}

public sealed record HumanReviewTask(
    string Id,
    string ReportId,
    string Reason,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? AssignedReviewerId = null,
    string? Decision = null,
    string? Comments = null,
    DateTimeOffset? DecidedAtUtc = null);
