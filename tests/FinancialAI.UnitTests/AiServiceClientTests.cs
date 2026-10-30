using System.Net;
using System.Text;
using FinancialAI.Application.Contracts;
using FinancialAI.Infrastructure.AiService;
using Xunit;

namespace FinancialAI.UnitTests;

public class AiServiceClientTests
{
    private static AiServiceAnalyzeRequest SampleRequest() => new(
        CorrelationId: "corr-from-workflow",
        ReportId: "r-1",
        Messages: Array.Empty<AiMessage>(),
        Projections: new ProjectionPayload(Array.Empty<decimal>(), Array.Empty<decimal>()),
        RetrievedContext: Array.Empty<RetrievedContextItem>());

    [Fact]
    public async Task AnalyzeAsync_On502_CapturesBodyAndCorrelation()
    {
        var body = """{"detail":{"code":"ai_service_error","correlationId":"corr-from-python"}}""";
        var handler = new FakeHandler(HttpStatusCode.BadGateway, body);
        var client = new AiServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://test") });

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.AnalyzeAsync(SampleRequest(), default));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal("ai_service_error", ex.ErrorCode);
        Assert.Equal("corr-from-python", ex.CorrelationId);
        Assert.Contains("ai_service_error", ex.ResponseBody);
        Assert.Contains("ai_service_error", ex.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_On500WithStringDetail_StillThrowsTypedException()
    {
        var body = """{"detail":"Graph terminated without a review decision"}""";
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, body);
        var client = new AiServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://test") });

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.AnalyzeAsync(SampleRequest(), default));

        Assert.Equal(500, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
        Assert.Equal("corr-from-workflow", ex.CorrelationId);
    }

    [Fact]
    public async Task AnalyzeAsync_OnNonJsonErrorBody_StillThrowsTypedException()
    {
        var handler = new FakeHandler(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>");
        var client = new AiServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://test") });

        var ex = await Assert.ThrowsAsync<AiServiceException>(
            () => client.AnalyzeAsync(SampleRequest(), default));

        Assert.Equal(502, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
        Assert.Contains("<html>", ex.ResponseBody);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FakeHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(resp);
        }
    }
}
