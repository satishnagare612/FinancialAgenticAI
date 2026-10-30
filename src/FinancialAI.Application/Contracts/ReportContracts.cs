namespace FinancialAI.Application.Contracts;

public sealed record ReportRequestDto(
    string ReportType,
    int ProjectionYears,
    string Currency,
    bool IncludeScenarioAnalysis);

public sealed record ReportStateDto(
    string ReportId,
    string Status,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc,
    string? DownloadJsonUrl,
    string? DownloadPdfUrl);

public sealed record AiServiceAnalyzeRequest(
    string CorrelationId,
    string ReportId,
    IReadOnlyList<AiMessage> Messages,
    ProjectionPayload Projections,
    IReadOnlyList<RetrievedContextItem> RetrievedContext);

public sealed record AiMessage(string Role, string Content);

public sealed record ProjectionPayload(
    IReadOnlyList<decimal> BaseCase,
    IReadOnlyList<decimal> ConservativeCase);

public sealed record RetrievedContextItem(
    string SourceId,
    string Text,
    string? Uri);

public sealed record AiServiceAnalyzeResponse(
    string Narrative,
    IReadOnlyList<string> KeyRisks,
    double Confidence,
    IReadOnlyList<string> CitedSources,
    string Decision);
