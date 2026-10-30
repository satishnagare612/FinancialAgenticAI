using FluentAssertions;
using FinancialAI.Application.Projections;

namespace FinancialAI.UnitTests;

public class ProjectionCalculatorTests
{
    private readonly ProjectionCalculator _calculator = new();

    [Fact]
    public void ZeroRate_ZeroContribution_ReturnsInitialBalanceEveryYear()
    {
        var input = new ProjectionInput(1000m, 0m, 0, 0, 3);
        var result = _calculator.Calculate(input);

        result.Should().HaveCount(4);
        result.All(p => p.NominalBalance == 1000m).Should().BeTrue();
    }

    [Fact]
    public void OneYear_SixPercentAnnual_CompoundsCorrectly()
    {
        var input = new ProjectionInput(1000m, 0m, 0.06, 0, 1);
        var result = _calculator.Calculate(input);

        result[1].NominalBalance.Should().Be(1060.00m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidYears_Throws(int years)
    {
        var act = () => _calculator.Calculate(new ProjectionInput(1000m, 0m, 0.05, 0.02, years));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
