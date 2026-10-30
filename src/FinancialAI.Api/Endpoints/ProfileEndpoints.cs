using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Profiles;
using FinancialAI.Domain;

namespace FinancialAI.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet("/", async (
            IFinancialProfileRepository profiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var profile = await profiles.GetByUserIdAsync(userId, ct);
            return profile is null
                ? Results.NotFound(new { code = "profile_not_found" })
                : Results.Ok(ToDto(profile));
        });

        group.MapPut("/", async (
            FinancialProfileDto dto,
            IFinancialProfileRepository profiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            FinancialProfileValidator.Validate(dto);

            var userId = GetUserId(http);
            var existing = await profiles.GetByUserIdAsync(userId, ct);

            var profile = new FinancialProfile(
                Id: userId,
                UserId: userId,
                InitialBalance: dto.InitialBalance,
                MonthlyContribution: dto.MonthlyContribution,
                ExpectedReturnRate: dto.ExpectedReturnRate,
                InflationRate: dto.InflationRate,
                CreatedAtUtc: existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow);

            var saved = await profiles.UpsertAsync(profile, ct);
            return Results.Ok(ToDto(saved));
        });

        group.MapDelete("/", async (
            IFinancialProfileRepository profiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            var deleted = await profiles.DeleteAsync(userId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static string GetUserId(HttpContext http)
        => http.User.Identity?.Name
            ?? throw new InvalidOperationException(
                "Authenticated request has no Name claim. Check the JwtBearer " +
                "NameClaimType configuration — it must match the claim issued " +
                "by the token provider.");

    private static FinancialProfileStateDto ToDto(FinancialProfile p)
        => new(p.Id, p.UserId, p.InitialBalance, p.MonthlyContribution,
                p.ExpectedReturnRate, p.InflationRate, p.CreatedAtUtc);
}
