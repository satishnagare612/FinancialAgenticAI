using MathNet.Numerics;

namespace FinancialAI.Application.Projections;

public sealed record ProjectionInput(
    decimal InitialBalance,
    decimal PeriodicContribution,
    double AnnualReturnRate,
    double AnnualInflationRate,
    int Years);

public sealed record ProjectionPoint(
    int Period,
    decimal NominalBalance,
    decimal InflationAdjustedBalance);

public interface IProjectionCalculator
{
    IReadOnlyList<ProjectionPoint> Calculate(ProjectionInput input);
}

public static class ProjectionValidator
{
    public static void Validate(ProjectionInput input)
    {
        if (input.InitialBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(input.InitialBalance));
        if (input.PeriodicContribution < 0)
            throw new ArgumentOutOfRangeException(nameof(input.PeriodicContribution));
        if (input.Years is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(input.Years));
        if (input.AnnualReturnRate is < -0.99 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(input.AnnualReturnRate));
        if (input.AnnualInflationRate is < -0.99 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(input.AnnualInflationRate));
    }
}

public sealed class ProjectionCalculator : IProjectionCalculator
{
    public IReadOnlyList<ProjectionPoint> Calculate(ProjectionInput input)
    {
        ProjectionValidator.Validate(input);

        var points = new List<ProjectionPoint>(input.Years + 1);
        var nominal = input.InitialBalance;
        var real = input.InitialBalance;
        var monthlyRate = Math.Pow(1 + input.AnnualReturnRate, 1.0 / 12) - 1;
        var monthlyInflation = Math.Pow(1 + input.AnnualInflationRate, 1.0 / 12) - 1;

        for (var year = 0; year <= input.Years; year++)
        {
            if (year > 0)
            {
                for (var month = 0; month < 12; month++)
                {
                    nominal = nominal * (decimal)(1 + monthlyRate) + input.PeriodicContribution;
                    real = real * (decimal)(1 + monthlyRate) / (decimal)(1 + monthlyInflation)
                           + input.PeriodicContribution
                             / (decimal)Math.Pow(1 + input.AnnualInflationRate, year);
                }
            }

            points.Add(new ProjectionPoint(
                Period: year,
                NominalBalance: Math.Round(nominal, 2, MidpointRounding.AwayFromZero),
                InflationAdjustedBalance: Math.Round(real, 2, MidpointRounding.AwayFromZero)));
        }

        return points;
    }
}
