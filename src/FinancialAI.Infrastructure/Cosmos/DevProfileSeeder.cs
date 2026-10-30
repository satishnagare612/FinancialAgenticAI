using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Cosmos;

public sealed class DevProfileSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _env;
    private readonly ILogger<DevProfileSeeder> _logger;

    public DevProfileSeeder(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment env,
        ILogger<DevProfileSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return;

        using var scope = _scopeFactory.CreateScope();
        var profiles = scope.ServiceProvider
            .GetRequiredService<IFinancialProfileRepository>();

        const string userId = "anonymous";
        if (await profiles.GetByUserIdAsync(userId, ct) is not null) return;

        var profile = new FinancialProfile(
            Id: userId,
            UserId: userId,
            InitialBalance: 100_000m,
            MonthlyContribution: 500m,
            ExpectedReturnRate: 0.06,
            InflationRate: 0.025,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        await profiles.UpsertAsync(profile, ct);
        _logger.LogInformation(
            "Seeded development financial profile for user {UserId}.", userId);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}