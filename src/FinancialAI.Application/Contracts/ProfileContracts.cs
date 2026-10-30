namespace FinancialAI.Application.Contracts;

/// <summary>Inbound payload for creating or replacing the current user's profile.</summary>
public sealed record FinancialProfileDto(
    decimal InitialBalance,
    decimal MonthlyContribution,
    double ExpectedReturnRate,
    double InflationRate);

/// <summary>Outbound representation returned to the client.</summary>
public sealed record FinancialProfileStateDto(
    string Id,
    string UserId,
    decimal InitialBalance,
    decimal MonthlyContribution,
    double ExpectedReturnRate,
    double InflationRate,
    DateTimeOffset CreatedAtUtc);