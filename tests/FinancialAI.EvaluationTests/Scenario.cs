using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Projections;

namespace FinancialAI.EvaluationTests;

/// <summary>
/// One replay scenario loaded from Scenarios/{name}.json.
/// JSON property names use camelCase; the JsonSerializerOptions below
/// handles the mapping, so the records can keep PascalCase names.
/// </summary>
public sealed record Scenario
{
    public required string Name { get; init; }
    public required ProjectionInput Input { get; init; }
    public required IReadOnlyList<ProjectionPoint> ExpectedProjections { get; init; }
    public required IReadOnlyList<RetrievedContextItem> RetrievedContext { get; init; }
    public required string ExpectedDecision { get; init; }

    /// <summary>
    /// Builds the wire request the .NET → Python client would send.
    /// The Messages array mirrors what AnalysisExecutor produces, so the
    /// replay harness exercises the same prompt shape the runtime uses.
    /// </summary>
    public AiServiceAnalyzeRequest ToWireRequest(
        IReadOnlyList<ProjectionPoint> projections,
        string? correlationId = null,
        string? reportId = null)
    {
        return new AiServiceAnalyzeRequest(
            CorrelationId: correlationId ?? Guid.NewGuid().ToString("N"),
            ReportId: reportId ?? Guid.NewGuid().ToString("N"),
            Messages:
            [
                new AiMessage(
                    "system",
                    "Explain the projections cautiously and cite only supplied sources."),
                new AiMessage(
                    "user",
                    $"Scenario: {Name}. Years: {Input.Years}."),
            ],
            Projections: new ProjectionPayload(
                BaseCase: projections.Select(p => p.NominalBalance).ToList(),
                ConservativeCase: projections.Select(p => p.NominalBalance).ToList()),
            RetrievedContext: RetrievedContext);
    }
}

/// <summary>
/// The harness's return value. It exists so callers can assert on both the
/// deterministic projections and the AI response without re-running either.
/// </summary>
public sealed record ScenarioResult(
    IReadOnlyList<ProjectionPoint> Projections,
    AiServiceAnalyzeResponse AiResponse);

/// <summary>
/// Loads every *.json file under Scenarios/ into Scenario records.
/// Used by ScenarioTests.cs to drive an [Theory] over the full matrix.
/// </summary>
public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<Scenario> LoadAll(string? directory = null)
    {
        directory ??= Path.Combine(
            AppContext.BaseDirectory, "Scenarios");

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Scenario directory not found: {directory}. " +
                "Ensure the *.json files have CopyToOutputDirectory=PreserveNewest " +
                "in FinancialAI.EvaluationTests.csproj.");
        }

        return Directory
            .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".ai.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .Select(Load)
            .ToList();
    }

    public static Scenario Load(string path)
    {
        using var stream = File.OpenRead(path);
        var scenario = JsonSerializer.Deserialize<Scenario>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Scenario file deserialized to null: {path}");
        return scenario;
    }
}
