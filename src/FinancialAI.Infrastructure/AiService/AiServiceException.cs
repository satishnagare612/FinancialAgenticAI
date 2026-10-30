namespace FinancialAI.Infrastructure.AiService;

public sealed class AiServiceException : Exception
{
    public int StatusCode { get; }
    public string? CorrelationId { get; }
    public string? ErrorCode { get; }
    public string? ResponseBody { get; }

    public AiServiceException(
        string message,
        int statusCode,
        string? correlationId,
        string? errorCode,
        string? responseBody,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        CorrelationId = correlationId;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }
}
