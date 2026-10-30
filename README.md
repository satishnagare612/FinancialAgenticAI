# FinancialAI

Deterministic financial projection engine with an AI-assisted reporting workflow.

Projections run in .NET (Math.NET Numerics). An AI graph explains results, cites
retrieved context, and routes low-confidence reports for human review.

## Architecture

    Next.js 14 (web) --> .NET 8 Web API (api) --> Python FastAPI (ai)
                              |                          |
                              v                          v
                        Cosmos DB                  LangGraph + RAG
                              |
                        MassTransit (in-memory local / Azure Service Bus prod)

## Tech stack

| Layer        | Technology                                                         |
|--------------|--------------------------------------------------------------------|
| Frontend     | Next.js 14 App Router, React 18, TypeScript, Tailwind v4, shadcn/ui |
| API          | .NET 8 Web API, Clean Architecture                                  |
| Workflow     | Microsoft Agent Framework WorkflowBuilder graph                    |
| AI service   | Python 3.11, FastAPI, LangGraph, LangChain, Pydantic v2            |
| Persistence  | Azure Cosmos DB (SQL API)                                          |
| Messaging    | Azure Service Bus (prod), MassTransit in-memory (local)            |
| Auth         | JWT bearer; dev token issuer available in Development only         |

## Quick start

Prerequisites: Docker Desktop with Linux containers.

    docker compose up -d --build

Wait ~30 seconds, then:

    Invoke-RestMethod http://localhost:8080/healthz
    Invoke-RestMethod http://localhost:8080/readyz

| Service              | URL                          |
|----------------------|------------------------------|
| Web                  | http://localhost:3000        |
| API                  | http://localhost:8080        |
| AI service           | http://localhost:8000        |
| Cosmos Data Explorer | http://localhost:1234        |
| Cosmos health        | http://localhost:8082/ready  |

### Create a report

    $token = (Invoke-RestMethod -Method Post http://localhost:8080/api/auth/dev-token `
        -ContentType "application/json" -Body '{"userId":"anonymous"}').token

    $body = '{"reportType":"retirement-projection","projectionYears":20,"currency":"USD","includeScenarioAnalysis":true}'
    $report = Invoke-RestMethod -Method Post http://localhost:8080/api/reports `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "application/json" -Body $body

    Invoke-RestMethod "http://localhost:8080/api/reports/$($report.reportId)" `
        -Headers @{ Authorization = "Bearer $token" }

Open http://localhost:3000/report/{reportId} once status is `completed`.

## Development

    # .NET
    dotnet build FinancialAI.sln -c Release
    dotnet test tests\FinancialAI.UnitTests -c Release

    # Python
    Push-Location services\ai
    poetry install
    poetry run ruff check .
    poetry run mypy app
    poetry run pytest
    Pop-Location

    # Web
    Push-Location web
    npm ci
    npm run build
    Pop-Location

### Gated test

69 assertions across the API surface. Requires the stack running.

    .\scripts\api-gated-test.ps1

Expected: `TOTAL: 69 pass, 0 fail`.

## Production configuration

Set these on the API container:

| Variable                            | Source                                    |
|-------------------------------------|-------------------------------------------|
| `ASPNETCORE_ENVIRONMENT`            | `Production`                              |
| `Cosmos__ConnectionString`          | Key Vault ref or managed identity         |
| `Cosmos__BootstrapContainers`       | `false` (containers provisioned by Bicep) |
| `ServiceBus__Enabled`               | `true`                                    |
| `ServiceBus__ConnectionString`      | Key Vault ref                             |
| `Auth__SigningKey`                  | Key Vault ref -- not the dev value        |
| `Auth__Issuer` / `Auth__Audience`   | OIDC values                               |
| `Cors__AllowedOrigins__0`           | Production web origin                     |
| `AiService__BaseUrl`                | AI service URL                            |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights                          |

AI service production: `LLM_PROVIDER=real`, `LLM_API_KEY` (secret manager),
`LLM_BASE_URL`, `LLM_MODEL`. See `services/ai/.env.example`.

## Security notes

- The dev JWT signing key in `appsettings.Development.json` and
  `docker-compose.yml` is a **local development placeholder**. Never use it
  in production. Rotate via `Auth__SigningKey` from Key Vault.
- The Cosmos emulator key in `docker-compose.yml` is Microsoft's public
  emulator key, not a real credential.
- `/api/auth/dev-token` and `/throw` return 404 outside `Development`.

## Status

All 26 numbered issues plus 3 supplementary (13, 14, 15) and Milestone 8a
(CORS) are closed. Gated test at 69/69. Milestones 8b (real LLM provider)
and 8c (Bicep + deployment) pending.

## License

Add a `LICENSE` file of your choice. None is committed.