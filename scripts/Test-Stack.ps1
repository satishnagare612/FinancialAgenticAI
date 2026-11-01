#Requires -Version 7.0
<#
.SYNOPSIS
  End-to-end verification of the FinancialAI stack after F-002 through F-008 fixes.

.DESCRIPTION
  Exercises the full report lifecycle and asserts on every session fix:
    F-002  generatedAtUtc preserved across workflow steps
    F-003  CORS preflight from the Vercel origin
    F-004  Server-side token fetch (no stale cache)
    F-005  JWT challenge returns RFC 7807 body (application/problem+json)
    F-006  Workflow fails fast, never hangs
    F-007  JSON/PDF download proxies respond
    F-008  Real LLM produces narrative (or graceful fallback)

.PARAMETER SkipDownloads
  Skip the F-007 download proxy test.

.PARAMETER SkipCors
  Skip the F-003 CORS preflight test.

.PARAMETER PollTimeoutSeconds
  How long to wait for the report to reach a terminal state. Default 90.

.PARAMETER ReportId
  Test an existing report instead of creating a new one.

.NOTES
  PowerShell 7.0+ only. Run with `pwsh .\Test-Stack.ps1`.
#>
[CmdletBinding()]
param(
    [switch]$SkipDownloads,
    [switch]$SkipCors,
    [ValidateRange(10, 600)]
    [int]$PollTimeoutSeconds = 90,
    [string]$ReportId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ─── Config ──────────────────────────────────────────────────────────────────
$ApiAppName = 'financialai2-api'
$ResourceGroup = 'financialai-staging'
$VercelHost = 'financial-agentic-ai-xi.vercel.app'

# ─── Helpers ─────────────────────────────────────────────────────────────────
$script:Results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Id,
        [string]$Name,
        [bool]  $Passed,
        [string]$Detail = ''
    )
    $script:Results.Add([PSCustomObject]@{
            Id     = $Id
            Name   = $Name
            Passed = $Passed
            Detail = $Detail
        })
    $icon = $Passed ? '[PASS]' : '[FAIL]'
    $color = $Passed ? 'Green' : 'Red'
    Write-Host ("{0} {1,-12} {2}" -f $icon, $Id, $Name) -ForegroundColor $color
    if ($Detail) {
        Write-Host ("                {0}" -f $Detail) -ForegroundColor DarkGray
    }
}

function ConvertTo-IsoString {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.ToString('o') }
    if ($Value -is [DateTimeOffset]) { return $Value.ToString('o') }
    return [string]$Value
}

# Thin wrapper that always returns a uniform response object, regardless
# of status code. PS 7's -SkipHttpErrorCheck is the primitive that makes
# this possible — no exception handling needed.
function Get-HttpResponse {
    param(
        [string]$Uri,
        [string]$Method = 'GET',
        [hashtable]$Headers = @{},
        [int]$TimeoutSec = 30
    )
    $resp = Invoke-WebRequest `
        -Uri $Uri `
        -Method $Method `
        -Headers $Headers `
        -TimeoutSec $TimeoutSec `
        -SkipHttpErrorCheck `
        -ErrorAction Stop

    $ctRaw = $resp.Headers['Content-Type']
    $contentType = if ($ctRaw -is [array]) { [string]$ctRaw[0] } else { [string]$ctRaw }

    # Invoke-WebRequest returns .Content as byte[] for non-standard content
    # types like application/problem+json. [string] on byte[] yields the
    # literal text "System.Byte[]" — useless. Decode UTF-8 explicitly.
    $contentString = if ($resp.Content -is [byte[]]) {
        [System.Text.Encoding]::UTF8.GetString($resp.Content)
    }
    elseif ($null -eq $resp.Content) {
        ''
    }
    else {
        [string]$resp.Content
    }

    [PSCustomObject]@{
        StatusCode       = [int]$resp.StatusCode
        ContentType      = $contentType
        Content          = $contentString
        RawContentLength = [int64]$resp.RawContentLength
    }
}

# ─── Header ──────────────────────────────────────────────────────────────────
Write-Host "`n═══ FinancialAI Stack Test ═══" -ForegroundColor Cyan
Write-Host ("Started : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host ("PS      : {0}" -f $PSVersionTable.PSVersion.ToString())
Write-Host ("API app : {0} / {1}" -f $ApiAppName, $ResourceGroup)

# ─── Resolve API FQDN ────────────────────────────────────────────────────────
Write-Host "`nResolving API FQDN..." -ForegroundColor Cyan
try {
    $apiFqdn = az containerapp show `
        --name $ApiAppName `
        --resource-group $ResourceGroup `
        --query "properties.configuration.ingress.fqdn" `
        -o tsv 2>$null
}
catch {
    $apiFqdn = $null
}

if ([string]::IsNullOrWhiteSpace($apiFqdn)) {
    Write-Host "Failed to resolve API FQDN. Check 'az login' and container app name." -ForegroundColor Red
    exit 1
}
$apiFqdn = $apiFqdn.Trim()
$apiBase = "https://$apiFqdn"
Write-Host ("API     : {0}" -f $apiBase) -ForegroundColor DarkGray

# ─── HEALTH ──────────────────────────────────────────────────────────────────
try {
    $r = Get-HttpResponse -Uri "$apiBase/healthz" -TimeoutSec 5
    Add-Result 'HEALTH' 'API /healthz reachable' ($r.StatusCode -eq 200) ("status={0}" -f $r.StatusCode)
    if ($r.StatusCode -ne 200) {
        Write-Host "`nCannot proceed without a reachable API. Exiting." -ForegroundColor Red
        exit 1
    }
}
catch {
    Add-Result 'HEALTH' 'API /healthz reachable' $false $_.Exception.Message
    exit 1
}

# ─── READY ───────────────────────────────────────────────────────────────────
try {
    $r = Get-HttpResponse -Uri "$apiBase/readyz" -TimeoutSec 8
    Add-Result 'READY' 'API /readyz (Cosmos reachable)' ($r.StatusCode -eq 200) ("status={0}" -f $r.StatusCode)
}
catch {
    Add-Result 'READY' 'API /readyz (Cosmos reachable)' $false $_.Exception.Message
}

# ─── AUTH ────────────────────────────────────────────────────────────────────
try {
    $tok = Invoke-RestMethod -Uri "$apiBase/api/auth/dev-token" -Method Post -ContentType 'application/json' -Body '{"userId":"anonymous"}' -TimeoutSec 5
    $token = $tok.token
    Add-Result 'AUTH' 'POST /api/auth/dev-token' ($null -ne $token) ("token length: {0}" -f $token.Length)
}
catch {
    Add-Result 'AUTH' 'POST /api/auth/dev-token' $false $_.Exception.Message
    Write-Host "`nCannot proceed without a token. Exiting." -ForegroundColor Red
    exit 1
}
$authHeaders = @{ Authorization = "Bearer $token" }

# ─── F-005: JWT challenge returns RFC 7807 body ──────────────────────────────
try {
    $resp = Get-HttpResponse -Uri "$apiBase/api/reports/x" -Headers @{ Authorization = 'Bearer garbage' } -TimeoutSec 10
    $isProblem = $resp.ContentType -match 'application/problem\+json'
    $hasDetail = $resp.Content -match '"detail"'
    $hasTitle = $resp.Content -match '"title"'
    Add-Result 'F-005' 'JWT challenge returns RFC 7807 body' `
    ($resp.StatusCode -eq 401 -and $isProblem -and $hasDetail -and $hasTitle) `
    ("status={0} contentType={1} hasDetail={2} hasTitle={3}" -f $resp.StatusCode, $resp.ContentType, $hasDetail, $hasTitle)
}
catch {
    Add-Result 'F-005' 'JWT challenge returns RFC 7807 body' $false $_.Exception.Message
}

# ─── F-003: CORS preflight ───────────────────────────────────────────────────
if (-not $SkipCors) {
    try {
        $corsOrigin = "https://$VercelHost"
        $preflight = curl.exe -s -i -X OPTIONS "$apiBase/api/reports/x/detail" `
            -H "Origin: $corsOrigin" `
            -H "Access-Control-Request-Method: GET" `
            -H "Access-Control-Request-Headers: authorization" 2>&1 | Out-String
        $allowOrigin = $preflight -match "access-control-allow-origin:\s*$([regex]::Escape($corsOrigin))"
        Add-Result 'F-003' 'CORS preflight allows Vercel origin' $allowOrigin ("allowOrigin={0}" -f $allowOrigin)
    }
    catch {
        Add-Result 'F-003' 'CORS preflight allows Vercel origin' $false $_.Exception.Message
    }
}

# ─── Create (or reuse) report ────────────────────────────────────────────────
if (-not $ReportId) {
    Write-Host "`nCreating report..." -ForegroundColor Cyan
    try {
        $bodyJson = '{"reportType":"retirement-projection","projectionYears":20,"currency":"USD","includeScenarioAnalysis":true}'
        $created = Invoke-RestMethod -Uri "$apiBase/api/reports" -Method Post -Headers $authHeaders -ContentType 'application/json' -Body $bodyJson -TimeoutSec 10
        $ReportId = $created.reportId
        $postedCreatedAtUtc = $created.createdAtUtc
        Add-Result 'CREATE' 'POST /api/reports' ($null -ne $ReportId) ("reportId: {0}" -f $ReportId)
    }
    catch {
        Add-Result 'CREATE' 'POST /api/reports' $false $_.Exception.Message
        Write-Host "`nCannot proceed without a report. Exiting." -ForegroundColor Red
        exit 1
    }
}
else {
    Write-Host "`nTesting existing report: $ReportId" -ForegroundColor Cyan
    $postedCreatedAtUtc = $null
    Add-Result 'CREATE' ("Reusing report {0}" -f $ReportId) $true ''
}

# ─── F-006: Workflow reaches terminal state ─────────────────────────────────
Write-Host ("`nPolling report status (timeout {0}s)..." -f $PollTimeoutSeconds) -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$detail = $null
$terminal = @('completed', 'failed', 'needs_review')
$lastStatus = ''

while ($sw.Elapsed.TotalSeconds -lt $PollTimeoutSeconds) {
    try {
        $detail = Invoke-RestMethod -Uri "$apiBase/api/reports/$ReportId/detail" -Method Get -Headers $authHeaders -TimeoutSec 10
        if ($detail.status -ne $lastStatus) {
            Write-Host ("  [{0,3}s] status: {1}" -f [int]$sw.Elapsed.TotalSeconds, $detail.status) -ForegroundColor DarkGray
            $lastStatus = $detail.status
        }
        if ($terminal -contains $detail.status) { break }
    }
    catch {
        Write-Host ("  poll error: {0}" -f $_.Exception.Message) -ForegroundColor DarkGray
    }
    Start-Sleep -Seconds 3
}
$sw.Stop()

if ($null -eq $detail -or $terminal -notcontains $detail.status) {
    $stuckAt = $null -eq $detail ? '(no response)' : $detail.status
    Add-Result 'F-006' 'Workflow reached terminal state' $false ("stuck at {0} after {1}s" -f $stuckAt, [int]$sw.Elapsed.TotalSeconds)
    Write-Host "`nWorkflow did not complete." -ForegroundColor Red
    exit 1
}
Add-Result 'F-006' 'Workflow reached terminal state' $true ("{0} in {1}s" -f $detail.status, [int]$sw.Elapsed.TotalSeconds)

# ─── Terminal status assessment ─────────────────────────────────────────────
$goodStatuses = @('completed', 'needs_review')
$narrative = $detail.narrative
$narrativeLen = [string]::IsNullOrEmpty($narrative) ? 0 : $narrative.Length
Add-Result 'STATUS' ("Report status is {0}" -f $detail.status) `
($goodStatuses -contains $detail.status) `
("narrative present: {0}" -f ($narrativeLen -gt 0))

# ─── Projections ────────────────────────────────────────────────────────────
$projCount = @($detail.baseCaseProjections).Count
Add-Result 'PROJ' 'Base case projections populated' ($projCount -eq 21) ("count: {0} (expected 21)" -f $projCount)

# ─── F-002: generatedAtUtc preserved ────────────────────────────────────────
if ($postedCreatedAtUtc) {
    try {
        $postedIso = ConvertTo-IsoString $postedCreatedAtUtc
        $generatedIso = ConvertTo-IsoString $detail.generatedAtUtc
        $posted = [DateTimeOffset]::Parse($postedIso)
        $generated = [DateTimeOffset]::Parse($generatedIso)
        $deltaMs = [Math]::Abs(($generated - $posted).TotalMilliseconds)
        Add-Result 'F-002' 'generatedAtUtc preserved through workflow' ($deltaMs -lt 1000) `
        ("POST: {0}, detail: {1}, delta: {2}ms" -f $postedIso, $generatedIso, [int]$deltaMs)
    }
    catch {
        Add-Result 'F-002' 'generatedAtUtc preserved through workflow' $false $_.Exception.Message
    }
}
else {
    Add-Result 'F-002' 'generatedAtUtc preserved (skipped — existing report)' $true ''
}

# ─── F-008: narrative populated (real LLM vs fallback vs empty) ────────────
$trimmed = $narrativeLen -gt 0 ? $narrative.Trim() : ''
$isFallback = $trimmed -match '^Automated narrative unavailable'
$isRealLlm = ($trimmed.Length -ge 20) -and (-not $isFallback)

if ($isRealLlm) {
    Add-Result 'F-008' 'Narrative populated (real LLM)' $true ("length: {0} chars" -f $narrativeLen)
}
elseif ($isFallback) {
    Add-Result 'F-008' 'Narrative populated (fallback)' $true `
    ("length: {0} chars — LLM output not parseable; fallback fired. Workflow still completed." -f $narrativeLen)
}
else {
    Add-Result 'F-008' 'Narrative populated' $false ("narrative is empty ({0} chars)" -f $narrativeLen)
}

# ─── Key risks + cited sources ─────────────────────────────────────────────
$risksCount = @($detail.keyRisks).Count
Add-Result 'RISKS' 'Key risks populated' ($risksCount -ge 1) ("count: {0}" -f $risksCount)

$srcsCount = @($detail.citedSources).Count
Add-Result 'CITES' 'Cited sources populated' ($srcsCount -ge 1) ("count: {0}" -f $srcsCount)

# ─── F-007: Download proxies ────────────────────────────────────────────────
if (-not $SkipDownloads -and $detail.status -eq 'completed') {
    $vercelJson = "https://$VercelHost/api/reports/$ReportId/json"
    $vercelPdf = "https://$VercelHost/api/reports/$ReportId/pdf"

    try {
        $jr = Get-HttpResponse -Uri $vercelJson -TimeoutSec 30
        $jCt = $jr.ContentType
        Add-Result 'F-007-JSON' 'Vercel JSON download proxy' `
        ($jr.StatusCode -eq 200 -and $jCt -match 'application/json') `
        ("status={0} contentType={1} bytes={2}" -f $jr.StatusCode, $jCt, $jr.Content.Length)
    }
    catch {
        Add-Result 'F-007-JSON' 'Vercel JSON download proxy' $false $_.Exception.Message
    }

    try {
        $pr = Get-HttpResponse -Uri $vercelPdf -TimeoutSec 30
        $pCt = $pr.ContentType
        $pOk = $pr.StatusCode -eq 200 -and $pCt -match 'application/pdf' -and $pr.RawContentLength -gt 1000
        Add-Result 'F-007-PDF' 'Vercel PDF download proxy' $pOk `
        ("status={0} contentType={1} bytes={2}" -f $pr.StatusCode, $pCt, $pr.RawContentLength)
    }
    catch {
        Add-Result 'F-007-PDF' 'Vercel PDF download proxy' $false $_.Exception.Message
    }
}
elseif ($detail.status -ne 'completed') {
    Add-Result 'F-007-JSON' 'Vercel JSON download proxy (skipped — status not completed)' $true ''
    Add-Result 'F-007-PDF'  'Vercel PDF download proxy (skipped — status not completed)'  $true ''
}

# ─── Summary ───────────────────────────────────────────────────────────────
Write-Host "`n═══ Summary ═══" -ForegroundColor Cyan
$total = $script:Results.Count
$passed = @($script:Results | Where-Object Passed).Count
$failed = $total - $passed
$color = $failed -eq 0 ? 'Green' : 'Red'
Write-Host ("Checks: {0} / {1} passed, {2} failed" -f $passed, $total, $failed) -ForegroundColor $color

if ($failed -gt 0) {
    Write-Host "`nFailed checks:" -ForegroundColor Red
    $script:Results | Where-Object { -not $_.Passed } | ForEach-Object {
        Write-Host ("  {0,-12} {1}" -f $_.Id, $_.Name) -ForegroundColor Red
        if ($_.Detail) {
            Write-Host ("                {0}" -f $_.Detail) -ForegroundColor DarkGray
        }
    }
}

Write-Host "`nReport detail:" -ForegroundColor Cyan
Write-Host ("  reportId       : {0}" -f $ReportId)
Write-Host ("  status         : {0}" -f $detail.status)
Write-Host ("  narrative      : {0} chars" -f $narrativeLen)
Write-Host ("  keyRisks       : {0}" -f $risksCount)
Write-Host ("  citedSources   : {0}" -f $srcsCount)
Write-Host ("  confidence     : {0}" -f $detail.confidenceScore)
Write-Host ("  projections    : {0}" -f $projCount)
if ($narrativeLen -gt 0) {
    $preview = $narrativeLen -gt 300 ? ($narrative.Substring(0, 300) + '...') : $narrative
    Write-Host ("  narrative text : {0}" -f $preview) -ForegroundColor DarkGray
}

Write-Host ("`nCompleted at {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host ("Report page: https://{0}/report/{1}" -f $VercelHost, $ReportId) -ForegroundColor Cyan

exit ($failed -eq 0 ? 0 : 1)