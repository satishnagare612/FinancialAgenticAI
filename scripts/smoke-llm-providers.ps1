<#
.SYNOPSIS
  Smoke-test each configured LLM preset against the running AI container.

.DESCRIPTION
  For each preset name passed, sets LLM_PROVIDER=real, LLM_PRESET=<name>,
  restarts the ai container, creates a report, and reports the narrative
  length. A narrative matching the stub text means the provider call was
  bypassed (LLM_PROVIDER not applied).

  Requires: docker compose stack running (api, ai, cosmos), and a valid
  LLM_API_KEY set in the environment or .env at repo root.

.PARAMETER Presets
  One or more preset names. Default: openrouter.

.EXAMPLE
  .\scripts\smoke-llm-providers.ps1 -Presets openrouter,groq
#>
param(
    [string[]]$Presets = @('openrouter'),
    [string]$ApiKey = $env:LLM_API_KEY
)

if (-not $ApiKey) {
    Write-Error "LLM_API_KEY is not set. Export it or pass -ApiKey."
    exit 1
}

$ErrorActionPreference = 'Stop'
$env:LLM_API_KEY = $ApiKey

foreach ($preset in $Presets) {
    Write-Host "`n=== Provider: $preset ===" -ForegroundColor Cyan

    $env:LLM_PROVIDER = 'real'
    $env:LLM_PRESET = $preset

    # Recreate ai so it picks up the env vars
    docker compose up -d --force-recreate ai | Out-Null
    Start-Sleep -Seconds 15

    $aiUp = (docker compose ps ai --format '{{.State}}') -eq 'running'
    if (-not $aiUp) {
        Write-Host "  FAIL — ai container not running" -ForegroundColor Red
        docker compose logs --tail 20 ai
        continue
    }

    $token = (Invoke-RestMethod -Method Post http://localhost:8080/api/auth/dev-token `
        -ContentType 'application/json' -Body '{"userId":"anonymous"}').token

    $body = '{"reportType":"retirement-projection","projectionYears":20,"currency":"USD","includeScenarioAnalysis":true}'
    $report = Invoke-RestMethod -Method Post http://localhost:8080/api/reports `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType 'application/json' -Body $body

    $deadline = (Get-Date).AddSeconds(60)
    $detail = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        try {
            $detail = Invoke-RestMethod "http://localhost:8080/api/reports/$($report.reportId)/detail" `
                -Headers @{ Authorization = "Bearer $token" }
            if ($detail.narrative) { break }
        } catch { }
    }

    if (-not $detail -or -not $detail.narrative) {
        Write-Host "  FAIL — no narrative after 60s" -ForegroundColor Red
        docker compose logs --tail 20 ai | Select-String "analyze_failed|Error"
        continue
    }

    $isStub = $detail.narrative -match "Stub narrative"
    $len = $detail.narrative.Length
    if ($isStub) {
        Write-Host "  FAIL — narrative is the stub (provider not applied)" -ForegroundColor Red
    } else {
        Write-Host "  PASS — narrative length $len" -ForegroundColor Green
        Write-Host "  Preview: $($detail.narrative.Substring(0, [Math]::Min(120, $len)))"
        Write-Host "  Citations: $($detail.citedSources -join ', ')"
    }
}

# Reset to stub default
$env:LLM_PROVIDER = 'stub'
docker compose up -d --force-recreate ai | Out-Null
Write-Host "`nReset to stub." -ForegroundColor DarkGray
