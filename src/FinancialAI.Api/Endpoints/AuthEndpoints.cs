using FinancialAI.Api.Auth;

namespace FinancialAI.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        // Development-only token issuer. Returns 404 in any environment
        // other than Development — the same response as an unknown route,
        // so a production caller does not learn that this path exists.
        group.MapPost("/dev-token", (
            DevTokenRequest request,
            IConfiguration configuration,
            IHostEnvironment environment) =>
        {
            if (!environment.IsDevelopment())
            {
                return Results.NotFound();
            }

            var userId = string.IsNullOrWhiteSpace(request.UserId)
                ? "anonymous"
                : request.UserId;

            var token = DevTokenIssuer.Issue(userId, configuration);
            return Results.Ok(new DevTokenResponse(token, userId));
        })
        .AllowAnonymous();

        return app;
    }

    public sealed record DevTokenRequest(string UserId);
    public sealed record DevTokenResponse(string Token, string UserId);
}
