# FinancialAI — End-to-End Application Engineering Blueprint & Manual
**Document Version:** 4.0 (Unified)  
**Target Platform:** Production-ready financial projection and AI-assisted reporting platform

| Component | Technology |
| :--- | :--- |
| **Backend** | .NET 8 Web API with Clean Architecture |
| **Workflow Orchestration** | Microsoft Agent Framework (MAF) .NET graph workflow |
| **AI Microservice** | Python 3.11+, FastAPI, LangGraph, LangChain, Pydantic v2 |
| **Frontend** | Next.js 14 (App Router), React 18, TypeScript, Tailwind CSS, shadcn/ui |
| **Persistence & Messaging** | Azure Cosmos DB, Azure Service Bus, Transactional Outbox, MassTransit Saga State Machine |
| **Observability** | OpenTelemetry, Azure Monitor / Application Insights, Serilog, structlog |

---

## Scope
This is a single, self-contained implementation blueprint. It contains every command, file, and code block needed to build the system end-to-end: repository scaffold, deterministic projection engine, Cosmos DB persistence with transactional outbox, MassTransit Saga, MAF workflow graph, Python LangGraph AI service, Next.js frontend, Docker Compose local stack, test suites, evaluation harness, CI, and deployment pipelines. 

> **Rule:** Do not mark an item complete until the verification command for that item passes in the current environment.

---

## Table of Contents
1. [What the Application Does](#1-what-the-application-does)
2. [Prerequisites](#2-prerequisites)
3. [Repository & Environment Setup](#3-repository--environment-setup)
4. [.NET Solution & Package Management](#4-net-solution--package-management)
5. [Domain Models](#5-domain-models)
6. [Projection Engine (Deterministic Math)](#6-projection-engine-deterministic-math)
7. [Application Layer: Contracts, Ports, DI](#7-application-layer-contracts-ports-di)
8. [Infrastructure: Cosmos DB, Outbox, Messaging](#8-infrastructure-cosmos-db-outbox-messaging)
9. [MassTransit Saga State Machine](#9-masstransit-saga-state-machine)
10. [Idempotent Consumer](#10-idempotent-consumer)
11. [PDF Rendering Adapter](#11-pdf-rendering-adapter)
12. [MAF Workflow Graph (.NET)](#12-maf-workflow-graph-net)
13. [API Endpoints & Program.cs](#13-api-endpoints--programcs)
14. [Python AI Microservice](#14-python-ai-microservice)
15. [.NET → Python HTTP Contract](#15-net--python-http-contract)
16. [Next.js Frontend](#16-nextjs-frontend)
17. [Docker Compose & Local Infrastructure](#17-docker-compose--local-infrastructure)
18. [Configuration Reference](#18-configuration-reference)
19. [Testing Strategy](#19-testing-strategy)
20. [Evaluation Harness](#20-evaluation-harness)
21. [Observability & Logging](#21-observability--logging)
22. [CI Pipeline](#22-ci-pipeline)
23. [Deployment Pipeline](#23-deployment-pipeline)
24. [Local Developer Runbook](#24-local-developer-runbook)
25. [Milestones & Exit Criteria](#25-milestones--exit-criteria)
26. [Production Hardening Checklist](#26-production-hardening-checklist)
27. [Acceptance Checklist](#27-acceptance-checklist)
28. [Official Documentation References](#28-official-documentation-references)

---

## 1. What the Application Does
The application generates a financial report from a user's stored financial profile. Calculations are deterministic and performed in .NET. An AI workflow explains results, identifies risks, cites retrieved context, and routes uncertain reports for human review.

### End-to-end request flow
1. User submits a report request from the Next.js interface.
2. .NET API authenticates the caller, validates the request, and assigns a `CorrelationId`.
3. API loads the user's profile from Cosmos DB.
4. Deterministic projection service validates inputs and calculates projection arrays using Math.NET Numerics. Calculations are never delegated to an LLM.
5. Application maps projections and retrieved context into a stable wire contract and calls the Python AI service.
6. MAF graph coordinates the workflow: `LoadProfileAndValidate` $\rightarrow$ `CalculateProjections` $\rightarrow$ `RetrievalAgent` $\rightarrow$ `AnalysisAgent` $\rightarrow$ `ReviewAgent`.
7. `ReviewAgent` returns `approve`, `revise`, or `human_review`. The confidence threshold and revision cap are configuration-driven.
8. Below-threshold confidence or explicit `human_review` creates a `HumanReviewTask` and pauses final publication.
9. Report is persisted as structured JSON and rendered to PDF.
10. API returns `reportId` and status; frontend opens `/report/[id]`.

### Architecture at a glance
```text
Next.js 14 / React 18 / TypeScript
              |
              v
       .NET 8 Web API
              |
    +---------+----------+
    |                    |
    v                    v
Profile/Report       ReportWorkflow
Persistence          (Microsoft Agent Framework)
(Cosmos DB)          Retrieval -> Analysis -> Review
    |                              |
    |                              +--> HumanReviewAgent
    |                              |
    |                              v
    |                      FastAPI AI service
    |                      LangGraph + LangChain
    |                      Pydantic v2 + RAG
    |
    +---- Outbox / Saga state / checkpoint metadata
              |
              v
       Azure Service Bus
              |
              v
  OpenTelemetry -> Azure Monitor / App Insights
```

---

## 2. Prerequisites
Install and verify the following before creating the repository:
* Git
* .NET SDK 8.x (`dotnet --list-sdks`)
* Node.js 20.x and npm (`node --version`, `npm --version`)
* Python 3.11 or 3.12 (`python --version`)
* Poetry (`poetry --version`)
* Docker Desktop with Linux containers and Compose v2 (`docker version`, `docker compose version`)
* An editor (Visual Studio, VS Code, or Rider)
* An Azure subscription for production resources. Local emulators are for development only.

```powershell
dotnet --info
node --version
npm --version
python --version
poetry --version
docker version
docker compose version
```
*If any command is not found, install that tool and reopen the terminal.*

---

## 3. Repository & Environment Setup

```powershell
# 1. Initialize repository
mkdir FinancialAI
cd FinancialAI
git init

# 2. Create directory structure
mkdir -p src/FinancialAI.Domain
mkdir -p src/FinancialAI.Application/Projections
mkdir -p src/FinancialAI.Application/Workflow
mkdir -p src/FinancialAI.Application/Ports
mkdir -p src/FinancialAI.Application/Contracts
mkdir -p src/FinancialAI.Infrastructure/Cosmos
mkdir -p src/FinancialAI.Infrastructure/AiService
mkdir -p src/FinancialAI.Infrastructure/Messaging
mkdir -p src/FinancialAI.Infrastructure/Pdf
mkdir -p src/FinancialAI.Infrastructure/Workflow
mkdir -p src/FinancialAI.Api/Endpoints

mkdir -p tests/FinancialAI.UnitTests
mkdir -p tests/FinancialAI.EvaluationTests/Scenarios

mkdir -p services/ai/app/models
mkdir -p services/ai/app/agents
mkdir -p services/ai/app/rag
mkdir -p services/ai/tests

mkdir -p web/app/report/[id]
mkdir -p web/app/actions
mkdir -p web/lib
mkdir -p web/components

mkdir -p infra/servicebus
mkdir -p .github/workflows
```

### 3.1 Repository conventions
* **C#:** nullable reference types enabled, file-scoped namespaces, async methods with `CancellationToken`, records for transport DTOs, no secrets in source code.
* **Python:** type hints, Pydantic v2, Ruff, mypy, pytest.
* **TypeScript:** strict mode.
* **Folders:** `Domain`, `Application`, `Infrastructure`, `Api` for .NET; `app/`, `agents/`, `rag/`, `models/` for Python; `app/`, `components/`, `lib/` for the frontend.
* Every service-to-service request and every Service Bus message carries the same `CorrelationId`.
* Use configuration binding and secret managers. Never commit `.env`, keys, connection strings, tokens, or production profile data.

### 3.2 .env.example (never commit the real .env)
```dotenv
# System Environment Settings
ACCEPT_EULA=Y
MSSQL_SA_PASSWORD=YourStrong!Passw0rd2026
EMULATOR_HTTP_PORT=5300

# API Settings
Cosmos__ConnectionString=AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;
AiService__BaseUrl=http://localhost:8000
AiService__TimeoutSeconds=60
ServiceBus__ConnectionString=Endpoint=sb://localhost:5672/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmitter=true

# Next.js Settings
NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
```
*The `ACCEPT_EULA=Y` value above is a template. Accept applicable license terms only after reading them.*

---

## 4. .NET Solution & Package Management

```powershell
# Create solution and project files
dotnet new sln -n FinancialAI

dotnet new classlib -n FinancialAI.Domain -f net8.0 -o src/FinancialAI.Domain
dotnet new classlib -n FinancialAI.Application -f net8.0 -o src/FinancialAI.Application
dotnet new classlib -n FinancialAI.Infrastructure -f net8.0 -o src/FinancialAI.Infrastructure
dotnet new webapi -n FinancialAI.Api -f net8.0 -o src/FinancialAI.Api --no-https

dotnet new xunit -n FinancialAI.UnitTests -f net8.0 -o tests/FinancialAI.UnitTests
dotnet new xunit -n FinancialAI.EvaluationTests -f net8.0 -o tests/FinancialAI.EvaluationTests

# Add projects to solution
dotnet sln FinancialAI.slnx add src/FinancialAI.Domain/FinancialAI.Domain.csproj
dotnet sln FinancialAI.slnx add src/FinancialAI.Application/FinancialAI.Application.csproj
dotnet sln FinancialAI.slnx add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj
dotnet sln FinancialAI.slnx add src/FinancialAI.Api/FinancialAI.Api.csproj
dotnet sln FinancialAI.slnx add tests/FinancialAI.UnitTests/FinancialAI.UnitTests.csproj
dotnet sln FinancialAI.slnx add tests/FinancialAI.EvaluationTests/FinancialAI.EvaluationTests.csproj

# Project references
dotnet add src/FinancialAI.Application/FinancialAI.Application.csproj reference src/FinancialAI.Domain/FinancialAI.Domain.csproj
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj reference src/FinancialAI.Application/FinancialAI.Application.csproj src/FinancialAI.Domain/FinancialAI.Domain.csproj
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj reference src/FinancialAI.Application/FinancialAI.Application.csproj src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj
dotnet add tests/FinancialAI.UnitTests/FinancialAI.UnitTests.csproj reference src/FinancialAI.Domain/FinancialAI.Domain.csproj src/FinancialAI.Application/FinancialAI.Application.csproj
dotnet add tests/FinancialAI.EvaluationTests/FinancialAI.EvaluationTests.csproj reference src/FinancialAI.Application/FinancialAI.Application.csproj src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj
```

### 4.1 Install packages
```powershell
# Application
dotnet add src/FinancialAI.Application/FinancialAI.Application.csproj package MathNet.Numerics
dotnet add src/FinancialAI.Application/FinancialAI.Application.csproj package Microsoft.Extensions.Options
dotnet add src/FinancialAI.Application/FinancialAI.Application.csproj package Microsoft.Agents.AI.Workflows

# Infrastructure
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package Microsoft.Azure.Cosmos
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package Azure.Messaging.ServiceBus
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package MassTransit
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package MassTransit.Azure.Cosmos
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package MassTransit.Azure.ServiceBus.Core
dotnet add src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj package QuestPDF

# Api
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj package Serilog.AspNetCore
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj package Azure.Monitor.OpenTelemetry.AspNetCore
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj package OpenTelemetry.Extensions.Hosting
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/FinancialAI.Api/FinancialAI.Api.csproj package OpenTelemetry.Instrumentation.Http

# Tests
dotnet add tests/FinancialAI.UnitTests/FinancialAI.UnitTests.csproj package FluentAssertions
```

### 4.2 Layer responsibilities
| Layer | Responsibility |
| :--- | :--- |
| **Domain** | Financial profile, projection, report, review, and validation types. No Cosmos DB, HTTP, Service Bus, or LLM framework. |
| **Application** | Use cases, ports/interfaces, projection services, orchestration contracts, validation, workflow options. Coordinates work; no infrastructure SDKs. |
| **Infrastructure** | Cosmos DB repositories, AI-service HTTP adapter, Service Bus adapters, Outbox/Saga persistence, PDF renderer, telemetry adapters. |
| **Api** | Endpoints, authentication/authorization, request validation, DI, exception-to-HTTP mapping, health/readiness probes. |

### 4.3 License & version notes
* QuestPDF may require a commercial license for revenue-generating use. If unacceptable, use FossPDF (fork of QuestPDF’s last FOSS commit) or PeachPDF. Pin the exact version you validate; a FOSS fork is API-compatible at the last FOSS commit.
* `Microsoft.Agents.AI.Workflows` changed its event model in early 2026 (agent outputs now surface as `WorkflowOutputEvent` subclasses). Validate the exact `WorkflowBuilder` overloads against the official samples and pin the version.
* Cosmos DB Linux emulator vNext does not implement every production feature and does not enforce authentication. Use real Azure resources for integration tests of unsupported features.

---

## 5. Domain Models
**File:** `src/FinancialAI.Domain/FinancialProfile.cs`

```csharp
namespace FinancialAI.Domain;

public sealed record FinancialProfile(
    string Id,
    string UserId,
    decimal InitialBalance,
    decimal MonthlyContribution,
    double ExpectedReturnRate,
    double InflationRate,
    DateTimeOffset CreatedAtUtc);

public sealed record Report(
    string Id,
    string UserId,
    string Status,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<decimal> BaseCaseProjections,
    IReadOnlyList<decimal> ConservativeProjections,
    string? Narrative,
    IReadOnlyList<string> KeyRisks,
    IReadOnlyList<string> CitedSources,
    double ConfidenceScore,
    DateTimeOffset GeneratedAtUtc);

public sealed record OutboxMessage(
    string Id,
    string ReportId,
    string EventType,
    string Payload,
    DateTimeOffset CreatedAtUtc)
{
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public enum ReportStatus
{
    Pending,
    Running,
    NeedsReview,
    Completed,
    Failed,
}

public sealed record HumanReviewTask(
    string Id,
    string ReportId,
    string Reason,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? AssignedReviewerId = null,
    string? Decision = null,
    string? Comments = null,
    DateTimeOffset? DecidedAtUtc = null);
```

---

## 6. Projection Engine (Deterministic Math)
**File:** `src/FinancialAI.Application/Projections/ProjectionCalculator.cs`

```csharp
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
```

> **Numeric Discipline:** Money-facing values use `decimal`. Math.NET rate transforms return `double`; convert deliberately at the boundary. Accumulate in `decimal`; use `double` only for the rate factor. Tests assert exact values where rounding permits and explicit tolerances otherwise. The real-rate formula (annual discount of monthly contributions) should be reviewed against a documented model before being treated as authoritative for production reporting.

---

## 7. Application Layer: Contracts, Ports, DI

### 7.1 Contracts
**File:** `src/FinancialAI.Application/Contracts/ReportContracts.cs`

```csharp
namespace FinancialAI.Application.Contracts;

public sealed record ReportRequestDto(
    string ReportType,
    int ProjectionYears,
    string Currency,
    bool IncludeScenarioAnalysis);

public sealed record ReportStateDto(
    string ReportId,
    string Status,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc,
    string? DownloadJsonUrl,
    string? DownloadPdfUrl);

public sealed record AiServiceAnalyzeRequest(
    string CorrelationId,
    string ReportId,
    IReadOnlyList<AiMessage> Messages,
    ProjectionPayload Projections,
    IReadOnlyList<RetrievedContextItem> RetrievedContext);

public sealed record AiMessage(string Role, string Content);

public sealed record ProjectionPayload(
    IReadOnlyList<decimal> BaseCase,
    IReadOnlyList<decimal> ConservativeCase);

public sealed record RetrievedContextItem(
    string SourceId,
    string Text,
    string? Uri);

public sealed record AiServiceAnalyzeResponse(
    string Narrative,
    IReadOnlyList<string> KeyRisks,
    double Confidence,
    IReadOnlyList<string> CitedSources,
    string Decision);
```

### 7.2 Ports
**File:** `src/FinancialAI.Application/Ports/IProjectPorts.cs`

```csharp
using FinancialAI.Application.Contracts;
using FinancialAI.Domain;

namespace FinancialAI.Application.Ports;

public interface IFinancialProfileRepository
{
    Task<FinancialProfile?> GetByUserIdAsync(string userId, CancellationToken ct);
}

public interface IReportRepository
{
    Task<Report?> GetAsync(string reportId, CancellationToken ct);
    Task SaveAsync(Report report, CancellationToken ct);
    Task MarkCompletedAsync(string reportId, CancellationToken ct);
}

public interface IOutboxRepository
{
    Task WriteWithReportAsync(Report report, OutboxMessage message, CancellationToken ct);
}

public interface IReportRenderer
{
    byte[] Render(Report report);
}

public interface IAiServiceClient
{
    Task<AiServiceAnalyzeResponse> AnalyzeAsync(
        AiServiceAnalyzeRequest request, CancellationToken ct);
}

public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public interface IProcessedMessageStore
{
    Task<bool> ExistsAsync(string messageId, CancellationToken ct);
    Task RecordAsync(string messageId, CancellationToken ct);
}
```

### 7.3 Workflow options
**File:** `src/FinancialAI.Application/Workflow/ReportWorkflowOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace FinancialAI.Application.Workflow;

public sealed class ReportWorkflowOptions
{
    public const string SectionName = "ReportWorkflow";

    [Range(0.0, 1.0)]
    public double HumanReviewConfidenceThreshold { get; init; } = 0.7;

    [Range(0, 10)]
    public int MaximumRevisionAttempts { get; init; } = 2;
}
```

### 7.4 Application DI
**File:** `src/FinancialAI.Application/DependencyInjection.cs`

```csharp
using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialAI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IProjectionCalculator, ProjectionCalculator>();
        services.AddSingleton<ProjectionValidator>();

        services.AddOptions<ReportWorkflowOptions>()
            .BindConfiguration(ReportWorkflowOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
```

---

## 8. Infrastructure: Cosmos DB, Outbox, Messaging

### 8.1 Cosmos repositories
**File:** `src/FinancialAI.Infrastructure/Cosmos/CosmosRepositories.cs`

```csharp
using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

public sealed class CosmosFinancialProfileRepository : IFinancialProfileRepository
{
    private readonly Container _container;

    public CosmosFinancialProfileRepository(CosmosClient client)
        => _container = client.GetContainer("financialai", "profiles");

    public async Task<FinancialProfile?> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid")
            .WithParameter("@uid", userId);

        using var iterator = _container.GetItemQueryIterator<FinancialProfile>(query);
        var response = await iterator.ReadNextAsync(ct);
        return response.FirstOrDefault();
    }
}

public sealed class CosmosReportRepository : IReportRepository
{
    private readonly Container _container;

    public CosmosReportRepository(CosmosClient client)
        => _container = client.GetContainer("financialai", "reports");

    public async Task<Report?> GetAsync(string reportId, CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<Report>(
                reportId, new PartitionKey(reportId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(Report report, CancellationToken ct)
        => await _container.UpsertItemAsync(report, new PartitionKey(report.Id), cancellationToken: ct);

    public async Task MarkCompletedAsync(string reportId, CancellationToken ct)
    {
        var existing = await GetAsync(reportId, ct)
            ?? throw new InvalidOperationException($"Report {reportId} not found.");
        var updated = existing with { Status = "completed" };
        await _container.UpsertItemAsync(updated, new PartitionKey(reportId), cancellationToken: ct);
    }
}
```

### 8.2 Cosmos outbox repository
**File:** `src/FinancialAI.Infrastructure/Cosmos/CosmosOutboxRepository.cs`

```csharp
using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;

namespace FinancialAI.Infrastructure.Cosmos;

public sealed class CosmosOutboxRepository : IOutboxRepository
{
    private readonly Container _reportsContainer;

    public CosmosOutboxRepository(CosmosClient client)
    {
        // Outbox documents live in the same container as reports so a single
        // transactional batch (same partition key = reportId) can write both.
        _reportsContainer = client.GetContainer("financialai", "reports");
    }

    public async Task WriteWithReportAsync(
        Report report, OutboxMessage message, CancellationToken ct)
    {
        var batch = _reportsContainer.CreateTransactionalBatch(new PartitionKey(report.Id));
        batch.UpsertItem(report);
        batch.CreateItem(message);

        using var response = await batch.ExecuteAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Outbox batch failed: {response.StatusCode} - {response.ErrorMessage}");
        }
    }
}
```

### 8.3 Outbox dispatcher
**File:** `src/FinancialAI.Infrastructure/Messaging/ServiceBusOutboxDispatcher.cs`

```csharp
using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Messaging;

public sealed class ServiceBusOutboxDispatcher : BackgroundService
{
    private readonly CosmosClient _cosmos;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<ServiceBusOutboxDispatcher> _logger;

    public ServiceBusOutboxDispatcher(
        CosmosClient cosmos,
        IMessagePublisher publisher,
        ILogger<ServiceBusOutboxDispatcher> logger)
    {
        _cosmos = cosmos;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var container = _cosmos.GetContainer("financialai", "reports");

        while (!stoppingToken.IsCancellationRequested)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = 'outbox' AND c.publishedAtUtc = null");

            using var iterator = container.GetItemQueryIterator<OutboxMessage>(query);
            while (iterator.HasMoreResults)
            {
                foreach (var message in await iterator.ReadNextAsync(stoppingToken))
                {
                    await _publisher.PublishAsync(message, stoppingToken);

                    message.PublishedAtUtc = DateTimeOffset.UtcNow;
                    await container.ReplaceItemAsync(
                        message, message.Id,
                        new PartitionKey(message.ReportId),
                        cancellationToken: stoppingToken);

                    _logger.LogInformation(
                        "Outbox message {MessageId} published for report {ReportId}.",
                        message.Id, message.ReportId);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```
> **Reliability Invariant:** Business state and the outbox document are written in one transactional batch. The dispatcher publishes, then marks `publishedAtUtc`. A crash between publish and mark causes a republish — consumers must be idempotent (§10).

### 8.4 Service Bus publisher adapter
**File:** `src/FinancialAI.Infrastructure/Messaging/ServiceBusMessagePublisher.cs`

```csharp
using Azure.Messaging.ServiceBus;
using FinancialAI.Application.Ports;
using FinancialAI.Domain;

namespace FinancialAI.Infrastructure.Messaging;

public sealed class ServiceBusMessagePublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public ServiceBusMessagePublisher(ServiceBusClient client)
        => _sender = client.CreateSender("financial-ai-events");

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var sbMessage = new ServiceBusMessage(message.Payload)
        {
            MessageId = message.Id,
            CorrelationId = message.ReportId,
            ContentType = "application/json",
            Subject = message.EventType,
        };
        await _sender.SendMessageAsync(sbMessage, ct);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
```

### 8.5 Infrastructure DI
**File:** `src/FinancialAI.Infrastructure/DependencyInjection.cs`

```csharp
using Azure.Messaging.ServiceBus;
using FinancialAI.Application.Ports;
using FinancialAI.Infrastructure.AiService;
using FinancialAI.Infrastructure.Cosmos;
using FinancialAI.Infrastructure.Messaging;
using FinancialAI.Infrastructure.Pdf;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var cs = configuration["Cosmos:ConnectionString"]
                ?? configuration["Cosmos__ConnectionString"]
                ?? throw new InvalidOperationException("Cosmos:ConnectionString is required.");
            return new CosmosClient(cs);
        });

        services.AddScoped<IFinancialProfileRepository, CosmosFinancialProfileRepository>();
        services.AddScoped<IReportRepository, CosmosReportRepository>();
        services.AddScoped<IOutboxRepository, CosmosOutboxRepository>();

        services.AddSingleton(sp =>
        {
            var cs = configuration["ServiceBus:ConnectionString"]
                ?? configuration["ServiceBus__ConnectionString"];
            return string.IsNullOrEmpty(cs)
                ? new ServiceBusClient("Endpoint=sb://localhost/;SharedAccessKeyName=x;SharedAccessKey=y")
                : new ServiceBusClient(cs);
        });
        services.AddSingleton<IMessagePublisher, ServiceBusMessagePublisher>();
        services.AddHostedService<ServiceBusOutboxDispatcher>();

        services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["AiService:BaseUrl"]
                ?? configuration["AiService__BaseUrl"]
                ?? throw new InvalidOperationException("AiService:BaseUrl is required."));
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue("AiService:TimeoutSeconds", 60));
        });

        services.AddSingleton<IReportRenderer, QuestPdfReportRenderer>();

        return services;
    }
}
```

---

## 9. MassTransit Saga State Machine
**File:** `src/FinancialAI.Infrastructure/Messaging/ReportSaga.cs`

```csharp
using MassTransit;

namespace FinancialAI.Infrastructure.Messaging;

public record ReportRequested(Guid ReportId);
public record ReviewRequired(Guid ReportId);
public record ReportCompleted(Guid ReportId);

public sealed class ReportSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public string ReportId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class ReportSaga : MassTransitStateMachine<ReportSagaState>
{
    public State Running { get; private set; } = null!;
    public State AwaitingReview { get; private set; } = null!;
    public State Completed { get; private set; } = null!;

    public Event<ReportRequested> ReportRequested { get; private set; } = null!;
    public Event<ReportCompleted> ReportCompleted { get; private set; } = null!;
    public Event<ReviewRequired> ReviewRequired { get; private set; } = null!;

    public ReportSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ReportRequested, e => e.CorrelateById(ctx => ctx.Message.ReportId));
        Event(() => ReportCompleted, e => e.CorrelateById(ctx => ctx.Message.ReportId));
        Event(() => ReviewRequired, e => e.CorrelateById(ctx => ctx.Message.ReportId));

        Initially(
            When(ReportRequested)
                .Then(ctx =>
                {
                    ctx.Saga.ReportId = ctx.Message.ReportId.ToString();
                    ctx.Saga.Status = "running";
                    ctx.Saga.CreatedAtUtc = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Running));

        During(Running,
            When(ReviewRequired)
                .Then(ctx => ctx.Saga.Status = "needs_review")
                .TransitionTo(AwaitingReview),
            When(ReportCompleted)
                .Then(ctx =>
                {
                    ctx.Saga.Status = "completed";
                    ctx.Saga.CompletedAtUtc = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Completed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
```

### 9.1 MassTransit registration (in Program.cs)
```csharp
services.AddMassTransit(cfg =>
{
    cfg.AddSagaStateMachine<ReportSaga, ReportSagaState>()
        .CosmosRepository(r =>
        {
            r.ConfigureEmulator(); // remove for production
            r.DatabaseId = "financialai";
            r.CollectionId = "sagas";
        });

    cfg.UsingAzureServiceBus((ctx, bus) =>
    {
        bus.ConnectionString = builder.Configuration["ServiceBus__ConnectionString"]
            ?? builder.Configuration["ServiceBus:ConnectionString"];
        bus.ConfigureEndpoints(ctx);
    });
});
```

---

## 10. Idempotent Consumer
**File:** `src/FinancialAI.Infrastructure/Messaging/ReportCompletedConsumer.cs`

```csharp
using FinancialAI.Application.Ports;
using MassTransit;

namespace FinancialAI.Infrastructure.Messaging;

public sealed class ReportCompletedConsumer : IConsumer<ReportCompleted>
{
    private readonly IProcessedMessageStore _store;
    private readonly IReportRepository _reports;

    public ReportCompletedConsumer(
        IProcessedMessageStore store, IReportRepository reports)
    {
        _store = store;
        _reports = reports;
    }

    public async Task Consume(ConsumeContext<ReportCompleted> context)
    {
        var messageId = context.MessageId
            ?? throw new InvalidOperationException("MessageId required.");

        if (await _store.ExistsAsync(messageId, context.CancellationToken))
            return;

        await _reports.MarkCompletedAsync(context.Message.ReportId.ToString(), context.CancellationToken);
        await _store.RecordAsync(messageId, context.CancellationToken);
    }
}
```
*`IProcessedMessageStore` can be backed by a Cosmos container with `id = messageId` and a TTL equal to the maximum redelivery window.*

---

## 11. PDF Rendering Adapter
**File:** `src/FinancialAI.Infrastructure/Pdf/QuestPdfReportRenderer.cs`

```csharp
using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinancialAI.Infrastructure.Pdf;

public sealed class QuestPdfReportRenderer : IReportRenderer
{
    public byte[] Render(Report report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header()
                    .Text($"Financial AI Projection Report — {report.Id}")
                    .SemiBold().FontSize(18);
                page.Content().Column(col =>
                {
                    col.Item().Text($"Generated Date: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
                    col.Item().Text("Assumptions").Bold();
                    col.Item().Text(string.Join("\n", report.Assumptions));
                    col.Item().Text("Narrative").Bold();
                    col.Item().Text(report.Narrative ?? "(No narrative provided)");
                    col.Item().Text("Key Risks").Bold();
                    col.Item().Text(string.Join("\n", report.KeyRisks.Select(r => $"• {r}")));
                    col.Item().Text("Cited Sources").Bold();
                    col.Item().Text(string.Join("\n", report.CitedSources));
                });
                page.Footer()
                    .Text("Disclaimer: This document is an automated projection report and does not constitute formal legal or financial advice.")
                    .FontSize(8).Italic();
            });
        }).GeneratePdf();
    }
}
```
*The JSON report is the machine-readable source of truth. The PDF is rendered from the same Report model, not a second AI generation pass.*

---

## 12. MAF Workflow Graph (.NET)

### 12.1 Graph shape
```text
StartReport
    |
    v
LoadProfileAndValidate
    |
    v
CalculateProjections
    |
    v
RetrievalAgent
    |
    v
AnalysisAgent
    |
    v
ReviewAgent
    |            \
    | approve     \ confidence below threshold / decision=human_review
    v              v
PersistAndRender   HumanReviewAgent
    |              |
    v              v
Complete           AwaitReviewerDecision
```
`revise` routes back to `AnalysisAgent` with a configuration-driven attempt limit. When the limit is reached, route to human review or fail safely per documented policy. Never allow infinite loops.

### 12.2 Workflow state
**File:** `src/FinancialAI.Application/Workflow/WorkflowState.cs`

```csharp
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Projections;

namespace FinancialAI.Application.Workflow;

public sealed record WorkflowState(
    Guid ReportId,
    string CorrelationId,
    string UserId,
    ProjectionInput? ProjectionInput,
    IReadOnlyList<ProjectionPoint>? Projections,
    IReadOnlyList<RetrievedContextItem>? RetrievedContext,
    AiServiceAnalyzeResponse? AiReview,
    ReportWorkflowOptions Options)
{
    public int RevisionCount { get; init; }
    public string? Status { get; init; }
    public string? Error { get; init; }
}
```

### 12.3 Review executor with configuration-driven routing
**File:** `src/FinancialAI.Infrastructure/Workflow/ReviewExecutor.cs`

```csharp
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class ReviewExecutor : Executor<WorkflowState, WorkflowState>
{
    private readonly IOptions<ReportWorkflowOptions> _options;

    public ReviewExecutor(IOptions<ReportWorkflowOptions> options) => _options = options;

    public override async Task<WorkflowState> ExecuteAsync(
        WorkflowState state, CancellationToken ct)
    {
        var review = state.AiReview
            ?? throw new InvalidOperationException("ReviewAgent did not return a review.");

        // Configuration-driven threshold: never bury 0.7 in executor code.
        if (review.Confidence < _options.Value.HumanReviewConfidenceThreshold
            && review.Decision == "approve")
        {
            review = review with
            {
                Decision = "human_review",
                KeyRisks = review.KeyRisks
                    .Append("Confidence below configured threshold.")
                    .ToList(),
            };
        }

        return state with { AiReview = review };
    }
}
```

### 12.4 Workflow registration
**File:** `src/FinancialAI.Infrastructure/Workflow/WorkflowRegistration.cs`

```csharp
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialAI.Infrastructure.Workflow;

public static class WorkflowRegistration
{
    public static IServiceCollection AddReportWorkflow(this IServiceCollection services)
    {
        services.AddSingleton<LoadProfileAndValidateExecutor>();
        services.AddSingleton<CalculateProjectionsExecutor>();
        services.AddSingleton<RetrievalExecutor>();
        services.AddSingleton<AnalysisExecutor>();
        services.AddSingleton<ReviewExecutor>();
        services.AddSingleton<HumanReviewExecutor>();
        services.AddSingleton<PersistAndRenderExecutor>();

        services.AddSingleton<Workflow>(sp =>
        {
            var load = sp.GetRequiredService<LoadProfileAndValidateExecutor>();
            var calc = sp.GetRequiredService<CalculateProjectionsExecutor>();
            var retrieval = sp.GetRequiredService<RetrievalExecutor>();
            var analysis = sp.GetRequiredService<AnalysisExecutor>();
            var review = sp.GetRequiredService<ReviewExecutor>();
            var humanReview = sp.GetRequiredService<HumanReviewExecutor>();
            var persist = sp.GetRequiredService<PersistAndRenderExecutor>();

            return new WorkflowBuilder(load)
                .AddEdge(load, calc)
                .AddEdge(calc, retrieval)
                .AddEdge(retrieval, analysis)
                .AddEdge(analysis, review)
                .AddEdge(persist, humanReview)
                .AddEdge<WorkflowState>(review, persist,
                    s => s.AiReview?.Decision is "approve" or "human_review")
                .AddEdge<WorkflowState>(review, analysis,
                    s => s.AiReview?.Decision == "revise"
                         && s.RevisionCount < s.Options.MaximumRevisionAttempts)
                .AddEdge<WorkflowState>(review, humanReview,
                    s => s.AiReview?.Decision == "revise"
                         && s.RevisionCount >= s.Options.MaximumRevisionAttempts)
                .WithOutputFrom(persist, humanReview)
                .Build();
        });

        return services;
    }
}
```
*Version note: The exact `AddEdge<TState>` overload and `WithOutputFrom` signature depend on the pinned `Microsoft.Agents.AI.Workflows` version. Validate against the official builder docs and a minimal prototype before adding branching.*

### 12.5 Checkpoints and resume
MAF checkpoints attach to workflow supersteps. Test in-memory first, then use durable storage appropriate to production. A checkpoint alone does not make external effects exactly-once; report persistence, Service Bus publishing, and PDF generation must use idempotency keys and transactional messaging patterns.

```csharp
[Fact]
public async Task Workflow_ResumesFromCheckpoint_WithoutDuplicatingReport()
{
    // Arrange: run to the persist step, capture checkpoint, simulate crash,
    // resume from checkpoint, assert exactly one report document exists.
    // Use an in-memory checkpoint provider and a fake renderer that
    // records invocation count.
}
```

---

## 13. API Endpoints & Program.cs

### 13.1 Endpoints
**File:** `src/FinancialAI.Api/Endpoints/ReportEndpoints.cs`

```csharp
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;
using FinancialAI.Domain;

namespace FinancialAI.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports");

        group.MapPost("/", async (
            ReportRequestDto request,
            IFinancialProfileRepository profiles,
            IReportRepository reports,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = http.User.Identity?.Name ?? "anonymous";
            var profile = await profiles.GetByUserIdAsync(userId, ct);
            if (profile is null) return Results.NotFound("Profile not found.");

            var reportId = Guid.NewGuid().ToString("N");
            var correlationId = http.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                ?? Guid.NewGuid().ToString("N");

            var report = new Report(
                reportId, userId, "pending", Array.Empty<string>(),
                Array.Empty<decimal>(), Array.Empty<decimal>(),
                null, Array.Empty<string>(), Array.Empty<string>(),
                0.0, DateTimeOffset.UtcNow);

            await reports.SaveAsync(report, ct);
            return Results.Ok(new ReportStateDto(
                reportId, "pending", correlationId, report.GeneratedAtUtc, null, null));
        });

        group.MapGet("/{id}", async (
            string id, IReportRepository reports, CancellationToken ct) =>
        {
            var report = await reports.GetAsync(id, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        group.MapGet("/{id}/json", async (
            string id, IReportRepository reports, CancellationToken ct) =>
        {
            var report = await reports.GetAsync(id, ct);
            return report is null ? Results.NotFound() : Results.Json(report);
        });

        group.MapGet("/{id}/pdf", async (
            string id, IReportRepository reports, IReportRenderer renderer,
            CancellationToken ct) =>
        {
            var report = await reports.GetAsync(id, ct);
            if (report is null) return Results.NotFound();
            var pdf = renderer.Render(report);
            return Results.File(pdf, "application/pdf", $"report-{id}.pdf");
        });

        return app;
    }
}
```

### 13.2 Program.cs
**File:** `src/FinancialAI.Api/Program.cs`

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;
using FinancialAI.Api.Endpoints;
using FinancialAI.Application;
using FinancialAI.Infrastructure;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReportWorkflow();
builder.Services.AddHealthChecks()
    .AddCheck<CosmosReadinessCheck>("cosmos", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FinancialAI.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .UseAzureMonitor();

builder.Services.AddMassTransit(cfg =>
{
    cfg.AddSagaStateMachine<FinancialAI.Infrastructure.Messaging.ReportSaga,
                            FinancialAI.Infrastructure.Messaging.ReportSagaState>()
        .CosmosRepository(r =>
        {
            r.ConfigureEmulator();
            r.DatabaseId = "financialai";
            r.CollectionId = "sagas";
        });
    cfg.UsingAzureServiceBus((ctx, bus) =>
    {
        bus.ConnectionString = builder.Configuration["ServiceBus__ConnectionString"]
            ?? builder.Configuration["ServiceBus:ConnectionString"]
            ?? "Endpoint=sb://localhost/;SharedAccessKeyName=x;SharedAccessKey=y";
        bus.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;
        await next();
    }
});

app.MapGet("/", () => Results.Ok(new { service = "FinancialAI.Api", version = "0.1.0" }));
app.MapReportEndpoints();
app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
app.MapHealthChecks("/readyz", new() { Predicate = c => c.Tags.Contains("ready") });

app.Run();

public partial class Program { }

public sealed class CosmosReadinessCheck : IHealthCheck
{
    private readonly CosmosClient _client;
    public CosmosReadinessCheck(CosmosClient client) => _client = client;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await _client.GetDatabase("financialai").ReadAsync(cancellationToken: cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cosmos unreachable", ex);
        }
    }
}
```

---

## 14. Python AI Microservice

### 14.1 Initialize
```powershell
cd services/ai
poetry init --name "financialai-ai" --version "0.1.0" --no-interaction
poetry add fastapi uvicorn pydantic pydantic-settings httpx langgraph langchain-core langchain-text-splitters
poetry add --group dev pytest pytest-cov ruff mypy
cd ../..
```
*Add only the model-provider SDK you actually use, and read credentials from environment variables or a secret manager. .NET never calls a model provider directly under this design.*

### 14.2 pyproject.toml (quality gates)
**File:** `services/ai/pyproject.toml` *(excerpt)*

```toml
[tool.pytest.ini_options]
testpaths = ["tests"]
pythonpath = ["."]
addopts = "-q"

[tool.ruff]
line-length = 100
target-version = "py311"

[tool.ruff.lint]
select = ["E", "F", "I", "UP", "B"]

[tool.mypy]
python_version = "3.11"
strict = true
mypy_path = "."
```

### 14.3 Pydantic schemas
**File:** `services/ai/app/models/responses.py`

```python
from typing import Literal
from pydantic import BaseModel, Field


class RetrievedContextItem(BaseModel):
    source_id: str
    text: str
    uri: str | None = None


class AnalysisResponse(BaseModel):
    narrative: str
    key_risks: list[str] = Field(default_factory=list)
    confidence: float = Field(ge=0.0, le=1.0)
    cited_sources: list[str] = Field(default_factory=list)


class ReviewResponse(BaseModel):
    approved: bool
    issues: list[str] = Field(default_factory=list)
    final_confidence: float = Field(ge=0.0, le=1.0)
    decision: Literal["approve", "revise", "human_review"]
```

**File:** `services/ai/app/models/requests.py`

```python
from pydantic import BaseModel
from app.models.responses import RetrievedContextItem


class ProjectionPayload(BaseModel):
    base_case: list[float]
    conservative_case: list[float]


class AnalyzeRequest(BaseModel):
    correlation_id: str
    report_id: str
    projections: ProjectionPayload
    retrieved_context: list[RetrievedContextItem]
```

### 14.4 LangGraph agent graph
**File:** `services/ai/app/agents/graph.py`

```python
from typing import TypedDict, Literal
from langgraph.graph import StateGraph, END
from langchain_core.language_models import BaseChatModel
from app.models.responses import AnalysisResponse, ReviewResponse, RetrievedContextItem


class AgentState(TypedDict):
    report_id: str
    correlation_id: str
    projections: dict
    retrieved_context: list[RetrievedContextItem]
    narrative: str | None
    key_risks: list[str]
    confidence: float
    cited_sources: list[str]
    review: ReviewResponse | None
    revision_count: int


def build_graph(llm: BaseChatModel, retriever, max_revisions: int = 2):
    structured_analysis = llm.with_structured_output(AnalysisResponse)
    structured_review = llm.with_structured_output(ReviewResponse)

    async def retrieval_node(state: AgentState) -> AgentState:
        items = await retriever.retrieve(
            query=f"financial context for report {state['report_id']}", top_k=5
        )
        state["retrieved_context"] = items
        return state

    async def analysis_node(state: AgentState) -> AgentState:
        result: AnalysisResponse = await structured_analysis.ainvoke([
            {
                "role": "system",
                "content": "Explain projections cautiously. Cite only supplied sources.",
            },
            {"role": "user", "content": str(state["projections"])},
        ])
        state.update(
            narrative=result.narrative,
            key_risks=result.key_risks,
            confidence=result.confidence,
            cited_sources=result.cited_sources,
        )
        return state

    async def review_node(state: AgentState) -> AgentState:
        supplied_ids = {c.source_id for c in state["retrieved_context"]}
        invalid_citations = [c for c in state["cited_sources"] if c not in supplied_ids]
        if invalid_citations:
            state["review"] = ReviewResponse(
                approved=False,
                issues=[f"Invalid citation(s): {invalid_citations}"],
                final_confidence=0.0,
                decision="revise",
            )
            state["revision_count"] += 1
            return state

        review: ReviewResponse = await structured_review.ainvoke([
            {
                "role": "system",
                "content": "Check numbers match deterministic projections and all claims are cited.",
            },
            {"role": "user", "content": state["narrative"] or ""},
        ])
        state["review"] = review
        if review.decision == "revise":
            state["revision_count"] += 1
        return state

    def route_after_review(state: AgentState) -> Literal["analysis", "end"]:
        review = state["review"]
        if review is None:
            return "end"
        if review.decision == "revise" and state["revision_count"] < max_revisions:
            return "analysis"
        return "end"

    graph = StateGraph(AgentState)
    graph.add_node("retrieval", retrieval_node)
    graph.add_node("analysis", analysis_node)
    graph.add_node("review", review_node)
    graph.set_entry_point("retrieval")
    graph.add_edge("retrieval", "analysis")
    graph.add_edge("analysis", "review")
    graph.add_conditional_edges(
        "review", route_after_review, {"analysis": "analysis", "end": END}
    )
    return graph.compile()
```

### 14.5 FastAPI main service
**File:** `services/ai/app/main.py`

```python
from contextlib import asynccontextmanager
from fastapi import FastAPI
from app.models.requests import AnalyzeRequest
from app.models.responses import AnalysisResponse


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Initialize retriever, LLM adapter, and vector index here.
    yield


app = FastAPI(title="FinancialAI AI Service", lifespan=lifespan)


@app.get("/healthz")
async def healthz():
    return {"status": "healthy"}


@app.get("/readyz")
async def readyz():
    # Check only dependencies required in this deployment mode.
    # Return 503 if a required dependency fails.
    return {"status": "ready"}


@app.post("/v1/analyze", response_model=AnalysisResponse)
async def analyze(payload: AnalyzeRequest):
    graph = app.state.graph
    result = await graph.ainvoke({
        "report_id": payload.report_id,
        "correlation_id": payload.correlation_id,
        "projections": payload.projections.model_dump(),
        "retrieved_context": [c.model_dump() for c in payload.retrieved_context],
        "narrative": None,
        "key_risks": [],
        "confidence": 0.0,
        "cited_sources": [],
        "review": None,
        "revision_count": 0,
    })
    return AnalysisResponse(
        narrative=result["narrative"] or "",
        key_risks=result["key_risks"],
        confidence=result["confidence"],
        cited_sources=result["cited_sources"],
    )
```

### 14.6 RAG pipeline outline
1. Retrieve source documents from approved sources.
2. Normalize text and metadata.
3. Chunk recursively with configurable size and overlap.
4. Generate embeddings with the selected provider.
5. Store vectors in FAISS (dev) or Azure AI Search (prod) behind a `VectorIndex` interface.
6. Query by similarity with tenant/access filters where applicable.
7. Re-rank and limit retrieved passages.
8. Pass source IDs, URIs, and passage text into the analysis request.
9. Validate every citation maps back to a real returned source record.

### 14.7 LoRA adapter (optional)
Treat LoRA as optional and model-specific. Implement an adapter interface first; load a model/adapter only when its runtime supports it. Configure model ID, adapter path, dtype/device, and token limits outside source code. Test base-model and adapter-enabled output against the same evaluation scenarios. Never claim a LoRA adapter is available until files, license, compatibility, and runtime are validated.

---

## 15. .NET → Python HTTP Contract

### 15.1 Wire contract
The C# `AiServiceAnalyzeRequest` must round-trip to JSON that validates against the Python Pydantic schemas. Add a contract test that asserts this.

```json
{
  "correlationId": "...",
  "reportId": "...",
  "messages": [
    { "role": "system", "content": "Explain the projections cautiously and cite supplied evidence." },
    { "role": "user", "content": "...validated projection data and retrieved context..." }
  ],
  "projections": {
    "baseCase": [1000, 1040, 1082],
    "conservativeCase": [1000, 1010, 1020]
  },
  "retrievedContext": [
    { "sourceId": "source-1", "text": "...", "uri": "..." }
  ]
}
```

### 15.2 Typed HttpClient adapter
**File:** `src/FinancialAI.Infrastructure/AiService/AiServiceClient.cs`

```csharp
using System.Net.Http.Json;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Ports;

namespace FinancialAI.Infrastructure.AiService;

public sealed class AiServiceClient : IAiServiceClient
{
    private readonly HttpClient _http;

    public AiServiceClient(HttpClient http) => _http = http;

    public async Task<AiServiceAnalyzeResponse> AnalyzeAsync(
        AiServiceAnalyzeRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/analyze")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("X-Correlation-Id", request.CorrelationId);

        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AiServiceAnalyzeResponse>(ct)
            ?? throw new InvalidOperationException("AI service returned an empty body.");
    }
}
```

### 15.3 Endpoint summary
| Method | Python route | Purpose |
| :--- | :--- | :--- |
| **GET** | `/healthz` | Process liveness |
| **GET** | `/readyz` | Required-dependency readiness |
| **POST** | `/v1/analyze` | Analysis operation with validated schemas |
| **POST** | `/v1/review` | Review operation if exposed separately |

*A composite endpoint is acceptable if responsibilities and schemas remain explicit. Provider keys live in Python configuration only.*

---

## 16. Next.js Frontend

### 16.1 Scaffold
```powershell
npx create-next-app@14 web --typescript --tailwind --eslint --app --src-dir=false --import-alias="@/*" --no-interactive
cd web
npm install
cd ..
```
Initialize `shadcn/ui` using the installed CLI and pinned Next.js/Tailwind versions. Inspect diffs rather than overwriting project configuration.

```powershell
cd web
npx shadcn@latest init
npx shadcn@latest add button card input textarea table progress badge dialog
cd ..
```

### 16.2 TypeScript types
**File:** `web/lib/types.ts`

```typescript
export type ReportStatus =
  | "pending"
  | "running"
  | "needs_review"
  | "completed"
  | "failed";

export interface ReportState {
  reportId: string;
  status: ReportStatus;
  correlationId: string;
  createdAtUtc: string;
  downloadJsonUrl?: string;
  downloadPdfUrl?: string;
}
```

### 16.3 API client
**File:** `web/lib/api.ts`

```typescript
import type { ReportState } from "./types";

const BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`API ${res.status}: ${body}`);
  }
  return res.json() as Promise<T>;
}

export async function createReport(input: {
  reportType: string;
  projectionYears: number;
  currency: string;
  includeScenarioAnalysis: boolean;
}): Promise<ReportState> {
  const res = await fetch(`${BASE}/api/reports`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(input),
    signal: AbortSignal.timeout(15_000),
  });
  return handle<ReportState>(res);
}

export async function getReport(id: string): Promise<ReportState> {
  const res = await fetch(`${BASE}/api/reports/${id}`, {
    signal: AbortSignal.timeout(10_000),
  });
  return handle<ReportState>(res);
}
```

### 16.4 Polling status component
**File:** `web/components/report-status.tsx`

```tsx
"use client";
import { useEffect, useState } from "react";
import { getReport } from "@/lib/api";
import type { ReportState } from "@/lib/types";

export function ReportStatus({ initial }: { initial: ReportState }) {
  const [state, setState] = useState(initial);

  useEffect(() => {
    if (state.status === "completed" || state.status === "failed") return;
    const interval = setInterval(async () => {
      try {
        setState(await getReport(state.reportId));
      } catch (err) {
        console.error("Failed to fetch status update", err);
      }
    }, 3000);
    return () => clearInterval(interval);
  }, [state.reportId, state.status]);

  return (
    <div className="p-4 border rounded shadow-sm bg-white">
      <div className="text-lg font-semibold">Status: {state.status}</div>
      <div className="text-sm text-gray-500">Report ID: {state.reportId}</div>
      {state.downloadPdfUrl && (
        <a
          href={state.downloadPdfUrl}
          className="mt-2 inline-block text-blue-600 underline"
        >
          Download PDF
        </a>
      )}
      {state.downloadJsonUrl && (
        <a
          href={state.downloadJsonUrl}
          className="mt-2 ml-3 inline-block text-blue-600 underline"
        >
          Download JSON
        </a>
      )}
    </div>
  );
}
```

### 16.5 Server action
**File:** `web/app/actions/create-report.ts`

```typescript
"use server";
import { createReport } from "@/lib/api";

export async function createReportAction(input: {
  reportType: string;
  projectionYears: number;
  currency: string;
  includeScenarioAnalysis: boolean;
}) {
  return createReport(input);
}
```

### 16.6 UI flow expectations
* **Profile form:** capture/confirm assumptions, validation, consent/disclosure.
* **Create report:** submit, render returned `reportId` and status.
* **Reports list:** pending/running/needs-review/completed/failed states.
* **Report detail:** assumptions, deterministic projection charts, narrative, risk factors, cited sources, review status, generation timestamp.
* **Download controls:** call JSON/PDF endpoints; handle authorization failures.
* **Human-review workflow:** exposed only to authorized reviewers.
* Accessible labels, keyboard navigation, responsive layout, and non-color-only status indicators. Assumptions and citations must be visible, not hidden in tooltips.

---

## 17. Docker Compose & Local Infrastructure

**File:** `infra/servicebus/Config.json`

```json
{
  "UserConfig": {
    "Namespaces": [
      {
        "Name": "sbemulatorns",
        "Queues": [
          {
            "Name": "financial-ai-work",
            "Properties": { "DeadLetteringOnMessageExpiration": true }
          }
        ],
        "Topics": [
          {
            "Name": "financial-ai-events",
            "Subscriptions": [
              {
                "Name": "report-generated",
                "Properties": { "MaxDeliveryCount": 10 }
              }
            ]
          }
        ]
      }
    ]
  }
}
```

**File:** `docker-compose.yml`

```yaml
version: '3.8'

services:
  cosmos:
    image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext
    container_name: financialai-cosmos
    ports:
      - "8081:8081"
      - "1234:1234"
      - "8082:8080"
    environment:
      - AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10
      - AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true

  ai:
    build:
      context: ./services/ai
    container_name: financialai-ai
    ports:
      - "8000:8000"
    environment:
      - PORT=8000

  api:
    build:
      context: .
      dockerfile: src/FinancialAI.Api/Dockerfile
    container_name: financialai-api
    ports:
      - "8080:8080"
    environment:
      - Cosmos__ConnectionString=AccountEndpoint=https://cosmos:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;
      - AiService__BaseUrl=http://ai:8000
    depends_on:
      - cosmos
      - ai

  web:
    build:
      context: ./web
    container_name: financialai-web
    ports:
      - "3000:3000"
    environment:
      - NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
    depends_on:
      - api
```

### 17.1 Port reference
| Service | Host URL/port | Notes |
| :--- | :--- | :--- |
| **Web** | `http://localhost:3000` | Next.js frontend |
| **API** | `http://localhost:8080` | .NET Web API |
| **AI service** | `http://localhost:8000` | FastAPI; keep private in production |
| **Cosmos gateway** | `https://localhost:8081` | HTTPS for .NET SDK emulator connection |
| **Cosmos Data Explorer** | `http://localhost:1234` | Dev explorer |
| **Cosmos health** | `http://localhost:8082/ready` on host *(container port 8080)* | Health API only; never the SDK gateway |
| **Service Bus** | host port `5672` | Emulator AMQP endpoint |
| **Service Bus health** | `http://localhost:5300/health` | Emulator management/health |

> **Port Warning:** The Cosmos emulator's internal port `8080` conflicts with the API host port `8080` if both are published directly. The mapping `8082:8080` keeps the emulator health API reachable on host port `8082` without a collision.

### 17.2 Cosmos emulator startup (standalone)
```powershell
docker run -d --name cosmos-emulator `
  -p 8081:8081 -p 1234:1234 -p 8082:8080 `
  -e AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10 `
  -e AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true `
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext

Start-Sleep -Seconds 10
Invoke-RestMethod http://localhost:8082/ready
```

---

## 18. Configuration Reference
**File:** `src/FinancialAI.Api/appsettings.json`

```json
{
  "ReportWorkflow": {
    "HumanReviewConfidenceThreshold": 0.7,
    "MaximumRevisionAttempts": 2
  },
  "AiService": {
    "BaseUrl": "http://localhost:8000",
    "TimeoutSeconds": 60
  },
  "Cosmos": {
    "ConnectionString": ""
  },
  "ServiceBus": {
    "ConnectionString": ""
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    }
  }
}
```
*Configuration precedence: environment variables override `appsettings.json`. Docker Compose uses `__` as the key separator (e.g., `Cosmos__ConnectionString`).*

---

## 19. Testing Strategy

### 19.1 .NET unit tests
**File:** `tests/FinancialAI.UnitTests/ProjectionCalculatorTests.cs`

```csharp
using FluentAssertions;
using FinancialAI.Application.Projections;
using Xunit;

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
```

**File:** `tests/FinancialAI.UnitTests/WorkflowRoutingTests.cs`

```csharp
using FluentAssertions;
using FinancialAI.Application.Contracts;
using FinancialAI.Application.Workflow;
using FinancialAI.Infrastructure.Workflow;
using Microsoft.Extensions.Options;
using Xunit;

public class WorkflowRoutingTests
{
    private static IOptions<ReportWorkflowOptions> Options(double threshold = 0.7)
        => Microsoft.Extensions.Options.Options.Create(
            new ReportWorkflowOptions
            {
                HumanReviewConfidenceThreshold = threshold,
                MaximumRevisionAttempts = 2,
            });

    [Fact]
    public async Task LowConfidence_RoutesToHumanReview()
    {
        var executor = new ReviewExecutor(Options());
        var state = new WorkflowState(
            Guid.NewGuid(), "corr", "user", null, null, null,
            new AiServiceAnalyzeResponse("n", [], 0.5, [], "approve"),
            Options().Value);

        var result = await executor.ExecuteAsync(state, default);
        result.AiReview!.Decision.Should().Be("human_review");
    }
}
```

### 19.2 Python tests
**File:** `services/ai/tests/test_schemas.py`

```python
import pytest
from pydantic import ValidationError
from app.models.responses import AnalysisResponse, ReviewResponse


def test_analysis_rejects_out_of_range_confidence():
    with pytest.raises(ValidationError):
        AnalysisResponse(narrative="n", confidence=1.5)


def test_review_rejects_unknown_decision():
    with pytest.raises(ValidationError):
        ReviewResponse(
            approved=True, final_confidence=0.9, decision="banana"  # type: ignore[arg-type]
        )
```

### 19.3 Integration tests
* Cosmos repository against emulator for supported features; real Azure for unsupported.
* Service Bus publish/consume, correlation IDs, dead-letter behavior, idempotent consumers.
* Outbox recovery: kill the dispatcher mid-cycle and assert no message lost.
* Readiness routes: 200 when dependencies healthy, 503 when required one is down.
* PDF structure and JSON/PDF value parity for the same Report.

### 19.4 Test commands
```powershell
dotnet test FinancialAI.sln -c Release

Push-Location services/ai
poetry run pytest
Pop-Location
```

---

## 20. Evaluation Harness
**File:** `tests/FinancialAI.EvaluationTests/ScenarioReplayHarness.cs`

```csharp
using FluentAssertions;
using FinancialAI.Application.Ports;
using FinancialAI.Application.Projections;

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
```

**File:** `tests/FinancialAI.EvaluationTests/Scenarios/retirement-projection-basic.json`

```json
{
  "name": "retirement-projection-basic",
  "input": {
    "initialBalance": 100000,
    "periodicContribution": 500,
    "annualReturnRate": 0.06,
    "annualInflationRate": 0.025,
    "years": 20
  },
  "expectedProjections": [
    { "period": 0, "nominalBalance": 100000, "inflationAdjustedBalance": 100000 }
  ],
  "retrievedContext": [
    {
      "sourceId": "src-001",
      "text": "Long-run equity returns historically...",
      "uri": "https://example.org/source-001"
    }
  ],
  "expectedDecision": "approve"
}
```

### 20.1 Minimum scenario matrix
* Low / high / zero return assumptions
* Missing or contradictory profile inputs
* Unsupported claim with no citation
* Hallucinated / invalid citation
* Low confidence requiring human review
* Reviewer asks for revision
* Revision limit reached
* Python service timeout
* Message redelivery / duplicate event
* Process failure after a checkpoint
* Report export retry

*Replay recorded scenarios in CI. Run the live-provider suite on demand only, gated by an explicit environment flag and API key from secrets.*

---

## 21. Observability & Logging

### 21.1 .NET
* Serilog with compact JSON output; `CorrelationId` and `ReportId` pushed via `LogContext`.
* OpenTelemetry for ASP.NET Core requests, outgoing HTTP calls, workflow spans.
* `UseAzureMonitor()` reads `APPLICATIONINSIGHTS_CONNECTION_STRING` in production.
* Capture latency, retries, workflow outcome, agent schema failures, review decisions, queue backlog.
* Never log credentials, raw prompts with PII, financial account numbers, tokens, or full profiles.

### 21.2 Python
* `structlog` JSON mode.
* Carry correlation and report IDs through every request and graph state.
* Record model/provider name, latency, token usage, retrieval source IDs, schema-validation failures, retries, evaluation labels.
* Redact sensitive text before logging.

### 21.3 Alerts (production)
Create alerts for: elevated report failure rate, queue backlog, repeated AI-service failures, readiness failures, review-queue aging, citation-validation failures, and latency regressions. Define ownership and runbooks for each.

---

## 22. CI Pipeline
**File:** `.github/workflows/ci.yml`

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - run: dotnet restore FinancialAI.sln
      - run: dotnet build FinancialAI.sln -c Release --no-restore
      - run: dotnet test FinancialAI.sln -c Release --no-build --collect:"XPlat Code Coverage"

  python:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: services/ai
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: "3.11"
      - uses: snok/install-poetry@v1
      - run: poetry install --no-interaction
      - run: poetry run ruff check .
      - run: poetry run mypy app
      - run: poetry run pytest --cov=app --cov-report=xml

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "20"
          cache: npm
          cache-dependency-path: web/package-lock.json
      - run: npm ci
      - run: npm run build
```
*Commit the Poetry lock file and `web/package-lock.json` after the first install. Use `npm ci` in CI.*

---

## 23. Deployment Pipeline
**File:** `.github/workflows/deploy.yml`

*Deploy on main after CI. Use OIDC federated credentials — no long-lived Azure publish profiles.*

```yaml
name: Deploy

on:
  workflow_run:
    workflows: [CI]
    types: [completed]
    branches: [main]

permissions:
  id-token: write
  contents: read

jobs:
  deploy-api:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - run: dotnet publish src/FinancialAI.Api -c Release -o ./publish
      - uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      - uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ secrets.AZURE_WEBAPP_NAME }}
          package: ./publish

  deploy-ai:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      - name: Build and push AI image
        run: |
          az acr build --registry ${{ secrets.ACR_NAME }} \
            --image financialai-ai:${{ github.sha }} services/ai
      - name: Deploy container to Container Apps
        run: |
          az containerapp update \
            --name financialai-ai \
            --resource-group ${{ secrets.AZURE_RG }} \
            --image ${{ secrets.ACR_NAME }}.azurecr.io/financialai-ai:${{ github.sha }}
```

*The web frontend deploys to Vercel or Azure Static Web Apps via native Git integration. Keep `NEXT_PUBLIC_API_BASE_URL` pointed at the production API.*

*Secrets required (GitHub Environment $\rightarrow$ production): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`, `AZURE_RG`, `ACR_NAME`. Configure federated credentials in Entra ID for the GitHub Actions app registration; do not store a client secret.*

---

## 24. Local Developer Runbook
Run each service independently first; introduce Compose only after individual services pass their own checks.

### 24.1 .NET
```powershell
dotnet restore FinancialAI.sln
dotnet build FinancialAI.sln -c Release --no-restore
dotnet test FinancialAI.sln -c Release --no-build
dotnet run --project src/FinancialAI.Api --urls http://localhost:8080
# In a second terminal:
Invoke-RestMethod http://localhost:8080/healthz
Invoke-RestMethod http://localhost:8080/readyz
```

### 24.2 Python
```powershell
Push-Location services/ai
poetry install
poetry run ruff check .
poetry run mypy app
poetry run pytest
poetry run uvicorn app.main:app --reload --port 8000
Pop-Location
# In a second terminal:
Invoke-RestMethod http://localhost:8000/healthz
Invoke-RestMethod http://localhost:8000/readyz
```

### 24.3 Web
```powershell
Push-Location web
npm ci
npm run build
npm run dev
Pop-Location
# Open http://localhost:3000
```

### 24.4 Docker Compose
```powershell
docker compose config --quiet
docker compose up --build -d
docker compose ps
docker compose logs --tail 200 api ai cosmos servicebus web
Invoke-RestMethod http://localhost:8080/healthz
Invoke-RestMethod http://localhost:8080/readyz
docker compose down
```
*Do not report the environment as ready based on Up status alone. Every required container must report healthy, and `/readyz` must return 200 with all required dependencies reachable.*

---

## 25. Milestones & Exit Criteria

### Milestone 1 — Scaffold
* Solution/projects, Python/Next.js directories.
* DI stubs and `/healthz` / `/readyz`.
* Editor config, ignore files, README, Dockerfiles, CI.
* **Exit:** `dotnet build` succeeds; `poetry install` + `pytest` pass; `npm ci` + `npm run build` pass; `/healthz` returns 200 for API and AI service.

### Milestone 2 — Contracts and persistence
* Domain types, request/response DTOs, repositories, auth.
* Cosmos health/readiness and repository tests.
* **Exit:** profile/report CRUD and access-control tests pass; outbox batch writes report + outbox in a single transaction (both docs asserted).

### Milestone 3 — Projection engine
* Assumptions, formulas, Math.NET calculators, scenario arrays.
* **Exit:** all `ProjectionCalculatorTests` pass (zero-rate, one-year, invalid inputs, rounding) with no AI calls.

### Milestone 4 — Python AI service and RAG
* Pydantic contracts, provider adapter, retrieval, chunking, vector index abstraction, analysis/review schemas.
* **Exit:** `test_schemas.py` rejects invalid confidence and malformed decisions; `test_graph.py` covers approve/revise/human-review routes; citation validation rejects a fabricated source; timeout and malformed-output tests pass.

### Milestone 5 — MAF graph workflow
* Typed executors and graph with `WorkflowBuilder`.
* Branching for approve/revise/human review, bounded retries.
* Checkpointing and event instrumentation.
* **Exit:** `WorkflowRoutingTests` cover approve $\rightarrow$ persist, revise $\rightarrow$ analysis (bounded), revise-exhausted $\rightarrow$ human review, low-confidence $\rightarrow$ human review; checkpoint/resume test passes without duplicate report.

### Milestone 6 — Messaging and Saga
* Queues/topics and message contracts.
* Transactional outbox, idempotent consumer, Saga transitions.
* **Exit:** outbox recovery test passes; Saga transitions Running $\rightarrow$ Completed on `ReportCompleted`; duplicate `MessageId` is skipped; crash/retry tests prove no silent event loss or duplicate report.

### Milestone 7 — Report output and UI
* JSON persistence, PDF rendering, report detail page, status refresh, download handling.
* **Exit:** end-to-end scenario creates a report, polls status, and downloads matching PDF/JSON; Playwright or manual verification confirms download links resolve.

### Milestone 8 — Production readiness
* Azure deployment, managed identity, Key Vault, monitoring, dashboards, quotas, alerts, DR, security review.
* **Exit:** staging deployment via `deploy.yml` succeeds; `/readyz` returns 503 when Cosmos is stopped and 200 when restored; Application Insights receives traces with `CorrelationId`; staging meets reliability, security, and supportability acceptance criteria.

---

## 26. Production Hardening Checklist
* [ ] Authentication/authorization enforced on all user/report/reviewer endpoints.
* [ ] Ownership validated on every profile and report resource.
* [ ] Secrets in Azure Key Vault or approved manager; removed from Compose and source.
* [ ] TLS between services and managed Azure endpoints.
* [ ] Local emulators replaced by Azure Cosmos DB, Azure Service Bus, Azure AI Search.
* [ ] Cosmos partitioning, throughput, indexing, backups, access policies, recovery validated.
* [ ] Transactional outbox implemented and recovery confirmed under process failure.
* [ ] MassTransit Saga transitions, retries, compensation, dead-letter monitoring implemented.
* [ ] Durable workflow checkpoints and resume tests pass.
* [ ] Every report operation and event has a stable idempotency key.
* [ ] Request size limits, timeouts, concurrency limits, rate limits configured.
* [ ] AI outputs validated against Pydantic/API schemas before use.
* [ ] Citations validated against retrieved source records.
* [ ] Human-review queue and reviewer authorization tested end-to-end.
* [ ] Confidence threshold and revision cap deployment-configurable and validated on startup.
* [ ] Data retention, privacy, consent, and deletion policies verified.
* [ ] Log/trace redaction implemented; access to financial artifacts restricted.
* [ ] Model/provider licensing, reliability, cost limits, fallback behavior evaluated.
* [ ] Package and container versions pinned; dependencies and images scanned.
* [ ] Dashboards and alerts for health, latency, costs, queue backlog, failure rate, review aging.
* [ ] PDF library licensing and accessibility reviewed.
* [ ] Financial assumptions and disclaimers reviewed by qualified stakeholders.
* [ ] `ProjectionCalculator` unit tests assert exact values for zero-rate, one-period, rounding-boundary cases.
* [ ] `ProjectionCalculatorTests` include a property-based test or randomized table asserting invariants.
* [ ] `Workflow_ResumesFromCheckpoint_WithoutDuplicatingReport` passes with the chosen checkpoint provider.
* [ ] Outbox dispatcher test proves no event loss when the process dies between publish and mark.
* [ ] Idempotent consumer test processes the same `MessageId` twice and asserts one side effect.
* [ ] Citation validation rejects a hallucinated source ID in a replay scenario.
* [ ] `ReviewExecutor` routes to human review when confidence < threshold even if model says approve.
* [ ] PDF and JSON report values match for the same `Report` model.
* [ ] CI pipeline runs on every PR and blocks merge on test failure.
* [ ] Deployment uses OIDC federated credentials, not stored publish profiles or client secrets.

---

## 27. Acceptance Checklist
```text
[ ] dotnet restore FinancialAI.sln
[ ] dotnet build FinancialAI.sln -c Release
[ ] dotnet test FinancialAI.sln -c Release
[ ] cd services/ai && poetry install
[ ] poetry run ruff check .
[ ] poetry run mypy app
[ ] poetry run pytest
[ ] cd web && npm ci && npm run build
[ ] docker compose config --quiet
[ ] docker compose up --build -d && all required containers healthy
[ ] API /healthz returns 200
[ ] API /readyz returns 200 when required dependencies healthy
[ ] API /readyz returns 503 when Cosmos is unavailable
[ ] AI /healthz and /readyz behave correctly
[ ] Projection tests confirm numerical invariants exactly or within stated tolerance
[ ] MAF workflow tests cover approve, revise, human-review, revision-exhausted branches
[ ] Review confidence threshold is configuration-driven and validated on startup
[ ] Revision loop has a validated maximum attempt count
[ ] Checkpoint/resume test passes without duplicate report
[ ] Outbox dispatcher recovery test passes
[ ] Idempotent consumer test passes
[ ] Citations resolve to actual retrieval results; hallucinated citation is rejected
[ ] PDF and JSON values match for the same report
[ ] Human-review task flow works end-to-end
[ ] Production secrets and sensitive data are not committed or logged
[ ] CI pipeline blocks merge on test failure
[ ] Deployment uses OIDC federated credentials
```
*A check is complete only after the exact command or automated test has passed in the current environment.*

---

## 28. Official Documentation References
* MAF workflow builder/execution: https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/builder-and-execution
* MAF checkpoints: https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints
* MAF source and .NET samples: https://github.com/microsoft/agent-framework
* MAF DI registration pattern: https://jesseliberty.com/2026/06/26/dependency-injection-agent-framework/
* Cosmos DB transactional outbox: https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos
* MassTransit Cosmos DB saga persistence: https://masstransit.io/documentation/configuration/persistence/azure-cosmos
* Idempotent Consumer pattern: https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-consumer
* QuestPDF / FossPDF licensing review: https://github.com/FossPDF/FossPDF.Net
* LangChain structured output / Pydantic v2: https://python.langchain.com/docs/how_to/structured_output/
* .NET 8 GitHub Actions CI/CD to Azure App Service: https://github.com/senthilts9/ci-cd-dotnet-demo
* Serilog + OpenTelemetry + Azure Monitor: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
* Azure Cosmos DB Linux emulator vNext: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux
* Azure Service Bus emulator local development: https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator
* Next.js 14 installation: https://nextjs.org/docs/14/getting-started/installation
* LangGraph documentation: https://langchain-ai.github.io/langgraph/
* FastAPI documentation: https://fastapi.tiangolo.com/
* Pydantic v2 documentation: https://docs.pydantic.dev/latest/
* Azure AI Search documentation: https://learn.microsoft.com/en-us/azure/search/
* Azure Monitor OpenTelemetry for .NET: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
* Math.NET Numerics: https://www.nuget.org/packages/MathNet.Numerics/

---

*This document is an engineering blueprint, not financial, tax, or legal advice. Reports must disclose assumptions and limitations. AI-generated narrative does not replace deterministic calculation checks or authorized human review where policy requires it.*
