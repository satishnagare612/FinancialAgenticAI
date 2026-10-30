using FinancialAI.Application.Contracts;

namespace FinancialAI.Application.Profiles;

public static class FinancialProfileValidator
{
    public static void Validate(FinancialProfileDto dto)
    {
        if (dto.InitialBalance < 0)
            throw new ArgumentOutOfRangeException(
                nameof(dto.InitialBalance), "Initial balance cannot be negative.");
        if (dto.MonthlyContribution < 0)
            throw new ArgumentOutOfRangeException(
                nameof(dto.MonthlyContribution), "Monthly contribution cannot be negative.");
        if (dto.ExpectedReturnRate is < -0.99 or > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(dto.ExpectedReturnRate), "Expected return must be between -0.99 and 1.0.");
        if (dto.InflationRate is < -0.99 or > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(dto.InflationRate), "Inflation rate must be between -0.99 and 1.0.");
    }
}
