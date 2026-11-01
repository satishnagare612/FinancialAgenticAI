#Requires -Version 7.0
<#
.SYNOPSIS
  Creates a report that will route to human review (needs_review status).

.DESCRIPTION
  Tries two approaches in order:
    1. Natural: extreme profile values + unusual projection horizon, hoping the
       model returns confidence < 0.7 (default threshold).
    2. Guaranteed: temporarily raise HumanReviewConfidenceThreshold to 0.95,
       creating a report, then optionally restore the original threshold.

.PARAMETER Guaranteed
  Skip approach 1 and go straight to the threshold override (deterministic).

.PARAMETER RestoreThreshold
  After the test, restore HumanReviewConfidenceThreshold to its original value.

.PARAMETER TestYears
  Projection horizon used in approach 1 (default 100 — long horizon
  tends to produce model uncertainty).
#>
[CmdletBinding()]
param(
    [switch]$Guaranteed,
    [switch]$RestoreThreshold,
    [ValidateRange(1, 100)]
    [int]$TestYears = 100
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$ApiAppName    = 'financialai2-api'
$ResourceGroup = 'financialai-staging'
$AiAppName     = 'financialai2-ai'

# ─── Helpers ─────────────────────────────────────────────────────────────────
function Get-ApiFqdn {
    (az containerapp show --name $ApiAppName --resource-group $ResourceGroup `
        --query "properties.configuration.ingress.fqdn" -o tsv).Trim()
}

function Get-Token($base) {
    (Invoke-RestMethod -Method Post "$base/api/auth/dev-token" `
        -ContentType 'application/json' -Body '{"userId":"anonymous"}').token
}

function Set-Profile($base, $headers, [hashtable]$profile) {
    $body = $profile | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri "$base/api/profile" -Method Put -Headers $headers `
        -ContentType 'application/json' -Body $body -TimeoutSec 10
}

function Get-CurrentThreshold {
    $val = az containerapp show --name $AiAppName --resource-group $ResourceGroup `
        --query "properties.template.containers[0].env[?name=='ReportWorkflow__HumanReviewConfidenceThreshold'].value | [0]" -o tsv
    if ([string]::IsNullOrWhiteSpace($val)) { return $null }
    return [double]::Parse($val.Trim(), [System.Globalization.CultureInfo]::InvariantCulture)
}

function Set-Threshold([double]$value) {
    az containerapp update --name $AiAppName --resource-group $ResourceGroup `
        --set-env-vars "ReportWorkflow__HumanReviewConfidenceThreshold=$value" 2>&1 | Out-Null
    Write-Host "  Threshold set to $value — waiting for revision promotion..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 60
}

function Create-ReportAndPoll($base, $headers, [int]$years) {
    $body = @{
        reportType             = 'retirement-projection'
        projectionYears        = $years
        currency               = 'USD'
        includeScenarioAnalysis = $true
    } | ConvertTo-Json -Compress

    $created = Invoke-RestMethod -Uri "$base/api/reports" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body $body -TimeoutSec 10
    Write-Host ("  Report: {0} (years={1})" -f $created.reportId, $years) -ForegroundColor DarkGray

    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        $detail = Invoke-RestMethod -Uri "$base/api/reports/$($created.reportId)/detail" `
            -Method Get -Headers $headers -TimeoutSec 10
        if ($detail.status -in @('completed', 'failed', 'needs_review')) { return $detail }
    }
    return $detail
}

# ─── Setup ──────────────────────────────────────────────────────────────────
Write-Host "`n═══ Create Low-Confidence Report ═══" -ForegroundColor Cyan

$apiFqdn = Get-ApiFqdn
if (-not $apiFqdn) { Write-Host "Could not resolve API FQDN" -ForegroundColor Red; exit 1 }
$base = "https://$apiFqdn"
$token = Get-Token $base
$authHeaders = @{ Authorization = "Bearer $token" }

Write-Host ("API: {0}" -f $base) -ForegroundColor DarkGray

# Save current profile so we can restore it
$currentProfile = Invoke-RestMethod -Uri "$base/api/profile" -Method Get -Headers $authHeaders -TimeoutSec 10
Write-Host "`nSaved current profile for restore:" -ForegroundColor DarkGray
Write-Host ("  InitialBalance: {0}, Contribution: {1}, Return: {2}, Inflation: {3}" -f `
    $currentProfile.initialBalance, $currentProfile.monthlyContribution, `
    $currentProfile.expectedReturnRate, $currentProfile.inflationRate)

$originalThreshold = Get-CurrentThreshold
Write-Host ("Current HumanReviewConfidenceThreshold: {0}" -f ($originalThreshold ?? 'default (0.7)'))

# ─── Approach 1: Extreme profile values (natural) ───────────────────────────
if (-not $Guaranteed) {
    Write-Host "`n[Approach 1] Extreme profile values" -ForegroundColor Cyan
    Write-Host "  Setting unusual values to make the model uncertain:" -ForegroundColor DarkGray
    Write-Host "    InitialBalance: 50000, MonthlyContribution: 0" -ForegroundColor DarkGray
    Write-Host "    ExpectedReturnRate: 0.35 (35%/yr — unrealistic)" -ForegroundColor DarkGray
    Write-Host "    InflationRate: -0.03 (-3%/yr — deflation)" -ForegroundColor DarkGray
    Write-Host ("    Projection horizon: {0} years" -f $TestYears) -ForegroundColor DarkGray

    Set-Profile $base $authHeaders @{
        initialBalance        = 50000
        monthlyContribution   = 0
        expectedReturnRate    = 0.35
        inflationRate         = -0.03
    } | Out-Null

    $detail = Create-ReportAndPoll $base $authHeaders $TestYears

    Write-Host ("  Result: status={0} confidence={1} narrativeLen={2}" -f `
        $detail.status, $detail.confidenceScore, $detail.narrative.Length) -ForegroundColor $(if ($detail.status -eq 'needs_review') { 'Green' } else { 'Yellow' })

    if ($detail.status -eq 'needs_review') {
        Write-Host "`n✓ Report routed to human review (natural path)" -ForegroundColor Green
        Write-Host ("  Report: {0}" -f $detail.reportId)
        Write-Host ("  Confidence: {0}" -f $detail.confidenceScore)
        Write-Host ("  Key risks: {0}" -f ($detail.keyRisks -join ' | '))
        Write-Host ("  Review URL: https://financial-agentic-ai-xi.vercel.app/report/{0}" -f $detail.reportId) -ForegroundColor Cyan

        # Restore profile
        Set-Profile $base $authHeaders @{
            initialBalance      = $currentProfile.initialBalance
            monthlyContribution = $currentProfile.monthlyContribution
            expectedReturnRate  = $currentProfile.expectedReturnRate
            inflationRate       = $currentProfile.inflationRate
        } | Out-Null
        Write-Host "`nProfile restored." -ForegroundColor DarkGray
        exit 0
    }

    Write-Host "`n  Model returned confidence={0} (>= 0.7). Natural path failed." -f $detail.confidenceScore -ForegroundColor Yellow
    Write-Host "  Falling through to Approach 2 (threshold override)." -ForegroundColor Yellow

    # Restore profile before approach 2
    Set-Profile $base $authHeaders @{
        initialBalance      = $currentProfile.initialBalance
        monthlyContribution = $currentProfile.monthlyContribution
        expectedReturnRate  = $currentProfile.expectedReturnRate
        inflationRate       = $currentProfile.inflationRate
    } | Out-Null
}

# ─── Approach 2: Raise threshold (guaranteed) ───────────────────────────────
Write-Host "`n[Approach 2] Raise HumanReviewConfidenceThreshold to 0.95" -ForegroundColor Cyan
Write-Host "  Any report with model confidence < 0.95 will route to human review." -ForegroundColor DarkGray

Set-Threshold 0.95

$detail = Create-ReportAndPoll $base $authHeaders 20

Write-Host ("  Result: status={0} confidence={1} narrativeLen={2}" -f `
    $detail.status, $detail.confidenceScore, $detail.narrative.Length) -ForegroundColor $(if ($detail.status -eq 'needs_review') { 'Green' } else { 'Red' })

if ($detail.status -eq 'needs_review') {
    Write-Host "`n✓ Report routed to human review (threshold override path)" -ForegroundColor Green
    Write-Host ("  Report: {0}" -f $detail.reportId)
    Write-Host ("  Confidence: {0} (below 0.95 threshold)" -f $detail.confidenceScore)
    Write-Host ("  Key risks: {0}" -f ($detail.keyRisks -join ' | '))
    Write-Host ("  Review URL: https://financial-agentic-ai-xi.vercel.app/report/{0}" -f $detail.reportId) -ForegroundColor Cyan

    if ($RestoreThreshold) {
        Write-Host "`nRestoring original threshold..." -ForegroundColor Cyan
        if ($null -eq $originalThreshold) {
            # Remove the env var entirely (falls back to appsettings default 0.7)
            az containerapp update --name $AiAppName --resource-group $ResourceGroup `
                --remove-env-vars "ReportWorkflow__HumanReviewConfidenceThreshold" 2>&1 | Out-Null
            Write-Host "  Removed ReportWorkflow__HumanReviewConfidenceThreshold (falls back to appsettings: 0.7)" -ForegroundColor DarkGray
        } else {
            Set-Threshold $originalThreshold
            Write-Host ("  Restored to {0}" -f $originalThreshold) -ForegroundColor DarkGray
        }
    } else {
        Write-Host "`n⚠ Threshold left at 0.95. Run with -RestoreThreshold or manually:" -ForegroundColor Yellow
        Write-Host "  az containerapp update --name $AiAppName --resource-group $ResourceGroup --remove-env-vars ReportWorkflow__HumanReviewConfidenceThreshold" -ForegroundColor Yellow
    }
    exit 0
}

Write-Host "`n✗ Report did not route to needs_review — unexpected." -ForegroundColor Red
Write-Host ("  status={0} confidence={1}" -f $detail.status, $detail.confidenceScore)
exit 1