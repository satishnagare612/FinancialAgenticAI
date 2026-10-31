using Azure.Monitor.OpenTelemetry.AspNetCore;
using FinancialAI.Api.Endpoints;
using FinancialAI.Api.ErrorHandling;
using FinancialAI.Application;
using FinancialAI.Infrastructure;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog ----------
builder.Host.UseSerilog((ctx, services, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

// ---------- Application + Infrastructure ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReportWorkflow();
builder.Services.AddHealthChecks()
    .AddCheck<CosmosReadinessCheck>("cosmos", tags: ["ready"]);

// ---------- Global exception handler ----------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---------- JWT authentication ----------
var signingKey = builder.Configuration["Auth:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    throw new InvalidOperationException(
        "Auth:SigningKey is required. Set it in appsettings.Development.json " +
        "for local development, or via a secret manager in production.");
}

builder.Services
    .AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"] ?? "FinancialAI",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Auth:Audience"] ?? "FinancialAI",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
        System.Text.Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
        };

        // A 401 with no body is opaque to the UI (lib/api.ts surfaces the
        // raw status). These events log the validation failure and return
        // an RFC 7807 body so a rejected token says *why* it was rejected.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>()
                    .LogWarning(ctx.Exception,
                        "JWT auth failed. Path={Path} FailureType={FailureType}",
                        ctx.Request.Path, ctx.Exception?.GetType().Name);
                return Task.CompletedTask;
            },
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                    title = "Unauthorized",
                    status = 401,
                    detail = ctx.AuthenticateFailure?.Message ?? "Token rejected.",
                    path = ctx.Request.Path.Value,
                });
            },
        };
    });

builder.Services.AddAuthorization();

// ---------- CORS ----------
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        if (allowedOrigins.Length == 1 && allowedOrigins[0] == "*")
        {
            // Explicit "*" in config means "allow all". AllowAnyOrigin()
            // is required — WithOrigins("*") would only match a literal
            // Origin: * header that browsers never send.
            // Safe with bearer tokens: Authorization is not a CORS credential.
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .WithExposedHeaders("X-Correlation-Id");
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .WithExposedHeaders("X-Correlation-Id");
        }
    });
});

// ---------- OpenTelemetry ----------
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FinancialAI.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation(o =>
        {
            o.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                if (request.Headers.TryGetValues("X-Correlation-Id", out var values))
                {
                    activity.SetTag("correlation.id", values.FirstOrDefault());
                }
            };
        }));

var appInsightsCs =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrWhiteSpace(appInsightsCs))
{
    otel.UseAzureMonitor();
}

// ---------- Build ----------
var app = builder.Build();

// ---------- Middleware pipeline ----------
app.UseExceptionHandler();

app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;
        await next();
    }
});

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

// ---------- Endpoints ----------
app.MapGet("/", () => Results.Ok(new { service = "FinancialAI.Api", version = "0.1.0" }));
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapReportEndpoints();
app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
app.MapHealthChecks("/readyz", new() { Predicate = c => c.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapGet("/throw", () =>
    {
        throw new InvalidOperationException(
            "Deliberate test exception for GlobalExceptionHandler.");
    });
}

app.Run();

public partial class Program { }

public sealed class CosmosReadinessCheck : IHealthCheck
{
    private readonly CosmosClient _client;
    public CosmosReadinessCheck(CosmosClient client) => _client = client;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await _client.GetDatabase("financialai").ReadAsync(cancellationToken: cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cosmos unreachable", ex);
        }
    }
}
