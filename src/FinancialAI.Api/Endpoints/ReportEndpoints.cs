using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Extensions.Options;

namespace FinancialAI.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization();

        group.MapPost("/", async (
            ReportRequestDto request,
            IFinancialProfileRepository profiles,
            IReportRepository reports,
            IReportWorkflowQueue workflowQueue,
            IOptions<ReportWorkflowOptions> workflowOptions,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var profile = await profiles.GetByUserIdAsync(userId, ct);
            if (profile is null)
            {
                return Results.Problem(
                    title: "Profile required",
                    detail: "Create a financial profile before requesting a report.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "profile_required",
                    });
            }

            if (request.ProjectionYears is < 1 or > 100)
            {
                return Results.Problem(
                    title: "Invalid projection years",
                    detail: "projectionYears must be between 1 and 100.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "invalid_projection_years",
                    });
            }

            var reportGuid = Guid.NewGuid();
            var reportId = reportGuid.ToString("N");
            var correlationId = http.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                ?? Guid.NewGuid().ToString("N");

            var initialStatus = ReportStatus.Pending.ToWireString();

            var report = new Report(
                reportId, userId, initialStatus, Array.Empty<string>(),
                Array.Empty<decimal>(), Array.Empty<decimal>(),
                null, Array.Empty<string>(), Array.Empty<string>(),
                0.0, DateTimeOffset.UtcNow)
            {
                CorrelationId = correlationId,
            };

            await reports.SaveAsync(report, ct);

            var workflowState = new WorkflowState(
                ReportId: reportGuid,
                CorrelationId: correlationId,
                UserId: userId,
                ProjectionInput: null,
                Projections: null,
                RetrievedContext: null,
                AiReview: null,
                Options: workflowOptions.Value)
            {
                ProjectionYears = request.ProjectionYears,
            };

            await workflowQueue.EnqueueAsync(workflowState, ct);

            return Results.Ok(ToStateDto(report));
        });

        group.MapGet("/{id}", async (
            string id, IReportRepository reports, HttpContext http, CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var report = await reports.GetAsync(id, ct);
            if (report is null || report.UserId != userId)
            {
                return Results.NotFound();
            }
            return Results.Ok(ToStateDto(report));
        });

        group.MapGet("/{id}/detail", async (
            string id, IReportRepository reports, HttpContext http, CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var report = await reports.GetAsync(id, ct);
            if (report is null || report.UserId != userId)
            {
                return Results.NotFound();
            }
            return Results.Ok(report);
        });

        group.MapGet("/{id}/json", async (
            string id, IReportRepository reports, HttpContext http, CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var report = await reports.GetAsync(id, ct);
            if (report is null || report.UserId != userId)
            {
                return Results.NotFound();
            }
            return Results.Json(report);
        });

        group.MapGet("/{id}/pdf", async (
            string id, IReportRepository reports, IReportRenderer renderer,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var report = await reports.GetAsync(id, ct);
            if (report is null || report.UserId != userId)
            {
                return Results.NotFound();
            }
            var pdf = renderer.Render(report);
            return Results.File(pdf, "application/pdf", $"report-{id}.pdf");
        });

        return app;
    }

    private static string GetUserId(HttpContext http)
        => http.User.Identity?.Name
            ?? throw new InvalidOperationException(
                "Authenticated request has no Name claim. Check the JwtBearer " +
                "NameClaimType configuration.");

    private static ReportStateDto ToStateDto(Report report)
    {
        var isCompleted = report.Status == ReportStatus.Completed.ToWireString();
        return new ReportStateDto(
            ReportId: report.Id,
            Status: report.Status,
            CorrelationId: report.CorrelationId,
            CreatedAtUtc: report.GeneratedAtUtc,
            DownloadJsonUrl: isCompleted
                ? $"/api/reports/{report.Id}/json"
                : null,
            DownloadPdfUrl: isCompleted
                ? $"/api/reports/{report.Id}/pdf"
                : null);
    }
}