using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinancialAI.Api.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        _logger.LogError(exception,
            "Unhandled exception. CorrelationId={CorrelationId} Path={Path} Method={Method}",
            correlationId, httpContext.Request.Path, httpContext.Request.Method);

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var statusCode = exception switch
        {
            ArgumentException or ArgumentOutOfRangeException
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = statusCode,
            Detail = "See server logs for the correlation ID.",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken);

        return true;
    }
}


