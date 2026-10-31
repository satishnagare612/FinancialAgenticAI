using System.Net.Http.Json;
using System.Text.Json;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.AiService;

public sealed class AiServiceClient : IAiServiceClient
{
    /// <summary>
    /// Hard ceiling on a single /v1/analyze call. Long enough for a real
    /// LLM, short enough that a stuck connection doesn't burn the whole
    /// workflow run budget.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _http;
    private readonly ILogger<AiServiceClient> _logger;

    public AiServiceClient(HttpClient http, ILogger<AiServiceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiServiceAnalyzeResponse> AnalyzeAsync(
        AiServiceAnalyzeRequest request, CancellationToken ct)
    {
        var url = new Uri(_http.BaseAddress!, "/v1/analyze");

        _logger.LogInformation(
            "AI call starting. ReportId={ReportId} Url={Url} Timeout={Timeout}",
            request.ReportId, url, RequestTimeout);

        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await _http.PostAsJsonAsync("/v1/analyze", request, linkedCts.Token);

            _logger.LogInformation(
                "AI call completed. ReportId={ReportId} Status={Status}",
                request.ReportId, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var (errorCode, correlationId) = ParseErrorBody(body);
                throw new AiServiceException(
                    $"AI service returned {(int)response.StatusCode} {response.ReasonPhrase}. " +
                    $"ErrorCode={errorCode ?? "(none)"} " +
                    $"CorrelationId={correlationId ?? request.CorrelationId}",
                    (int)response.StatusCode,
                    correlationId ?? request.CorrelationId,
                    errorCode,
                    body);
            }

            return await response.Content.ReadFromJsonAsync<AiServiceAnalyzeResponse>(ct)
                ?? throw new AiServiceException(
                    "AI service returned an empty body.",
                    (int)response.StatusCode,
                    request.CorrelationId,
                    "empty_response",
                    null);
        }
        catch (OperationCanceledException)
            when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError(
                "AI call TIMED OUT after {Timeout}. ReportId={ReportId} Url={Url}",
                RequestTimeout, request.ReportId, url);
            throw new TimeoutException(
                $"AI call exceeded {RequestTimeout.TotalSeconds:N0}s for report {request.ReportId}.");
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx,
                "AI call FAILED. ReportId={ReportId} Url={Url} Error={Error}",
                request.ReportId, url, httpEx.Message);
            throw new AiServiceException(
                $"AI service unreachable: {httpEx.Message}",
                statusCode: 0,
                correlationId: request.CorrelationId,
                errorCode: "ai_service_unreachable",
                responseBody: null,
                innerException: httpEx);
        }
    }

    private static (string? ErrorCode, string? CorrelationId) ParseErrorBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("detail", out var detail))
                return (null, null);

            // FastAPI shape 1: {"detail": {"code": "...", "correlationId": "..."}}
            if (detail.ValueKind == JsonValueKind.Object)
            {
                var code = detail.TryGetProperty("code", out var c) ? c.GetString() : null;
                var corr = detail.TryGetProperty("correlationId", out var co) ? co.GetString() : null;
                return (code, corr);
            }
            // FastAPI shape 2: {"detail": "..."} — no code or correlation
            return (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}