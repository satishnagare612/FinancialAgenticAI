using Xunit;

namespace FinancialAI.IntegrationTests;

/// <summary>
/// Fact that is skipped when the Cosmos emulator is unreachable.
/// Avoids red CI on developers or runners without Docker.
/// </summary>
public sealed class RequiresEmulatorFactAttribute : FactAttribute
{
    public RequiresEmulatorFactAttribute()
    {
        if (!EmulatorAvailable.Value)
        {
            Skip = "Cosmos emulator not reachable at https://localhost:8081.";
        }
    }

    private static readonly Lazy<bool> EmulatorAvailable = new(() =>
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
            return http.GetAsync("https://localhost:8081/_explorer/emulator.pem")
                       .GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch { return false; }
    });
}
