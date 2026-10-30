using FluentAssertions;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;
using FinancialAI.EvaluationTests;

public sealed class ScenarioReplayHarness
{
    private readonly IProjectionCalculator _calculator;
    private readonly IAiServiceClient _aiClient;

    public ScenarioReplayHarness(
        IProjectionCalculator calculator, IAiServiceClient aiClient)
    {
        _calculator = calculator;
        _aiClient = aiClient;
    }

    public async Task<ScenarioResult> RunAsync(Scenario scenario, CancellationToken ct)
    {
        var projections = _calculator.Calculate(scenario.Input);

        scenario.ExpectedProjections.Should().BeEquivalentTo(
            projections,
            o => o.WithStrictOrdering()
                  .Using<decimal>(c => c.Subject.Should()
                      .BeApproximately(c.Expectation, 0.01m))
                  .WhenTypeIs<decimal>());

        var aiResponse = await _aiClient.AnalyzeAsync(
            scenario.ToWireRequest(projections), ct);

        var suppliedIds = scenario.RetrievedContext
            .Select(c => c.SourceId).ToHashSet();

        aiResponse.CitedSources.Should().OnlyContain(
            id => suppliedIds.Contains(id),
            "every citation must resolve to a retrieved source");

        return new ScenarioResult(projections, aiResponse);
    }
}
