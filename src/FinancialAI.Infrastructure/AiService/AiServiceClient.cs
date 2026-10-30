using System.Net.Http.Json;
using System.Text.Json;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;

namespace FinancialAI.Infrastructure.AiService;

public sealed class AiServiceClient : IAiServiceClient
{
    private readonly HttpClient _http;

    public AiServiceClient(HttpClient http) => _http = http;

    public async Task<AiServiceAnalyzeResponse> AnalyzeAsync(
        AiServiceAnalyzeRequest request, CancellationToken ct)
    {
        // Combine the external cancellation token with a per-request timeout (e.g., 300s)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        // Use linkedCts.Token for the HTTP call
        var response = await _http.PostAsJsonAsync("/v1/analyze", request, linkedCts.Token);

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
