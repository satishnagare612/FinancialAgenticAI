# =========================================================
# Full API gated test — 53 checks across Issues 1,3,5,6,7,8,11,12,20,21
# =========================================================
$ErrorActionPreference = "Continue"
$script:results = [System.Collections.Generic.List[object]]::new()
$script:currentGate = ""

function Start-Gate {
    param([string]$Name)
    $script:currentGate = $Name
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
}

function Assert-Equal {
    param([string]$Name, $Actual, $Expected)
    $pass = ("$Actual" -eq "$Expected")
    $script:results.Add([PSCustomObject]@{
            Gate = $script:currentGate; Test = $Name
            Status = if ($pass) { "PASS" } else { "FAIL" }
            Expected = "$Expected"; Actual = "$Actual"
        })
    if ($pass) { Write-Host "  PASS  $Name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $Name  (expected: $Expected, got: $Actual)" -ForegroundColor Red }
}

function Assert-True {
    param([string]$Name, $Condition, [string]$Detail = "")
    $pass = [bool]$Condition
    $script:results.Add([PSCustomObject]@{
            Gate = $script:currentGate; Test = $Name
            Status = if ($pass) { "PASS" } else { "FAIL" }
            Expected = "true"; Actual = if ($pass) { "true" } else { "false $Detail" }
        })
    if ($pass) { Write-Host "  PASS  $Name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $Name  $Detail" -ForegroundColor Red }
}

function Invoke-Safe {
    param([hashtable]$Params)
    try {
        $r = Invoke-WebRequest @Params -UseBasicParsing -ErrorAction Stop
        return @{ Status = [int]$r.StatusCode; Body = $r.Content; Headers = $r.Headers }
    }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return @{ Status = $code; Body = $_.ErrorDetails.Message; Headers = @{} }
    }
}

$BASE = "http://localhost:8080"

# ---------------------------------------------------------
Start-Gate "G1 — Container health"
$ps = docker compose ps --format "json" | ConvertFrom-Json
foreach ($svc in @("cosmos", "ai", "api")) {
    $row = $ps | Where-Object { $_.Service -eq $svc }
    Assert-True "Container '$svc' is running" ($row -and $row.State -eq "running") "state=$($row.State)"
}
Assert-Equal "GET /healthz" (Invoke-Safe @{ Uri = "$BASE/healthz" }).Status 200
Assert-Equal "GET /readyz"  (Invoke-Safe @{ Uri = "$BASE/readyz" }).Status 200

# ---------------------------------------------------------
Start-Gate "G2 — Anonymous endpoints"
Assert-Equal "GET / without auth" (Invoke-Safe @{ Uri = "$BASE/" }).Status 200

# ---------------------------------------------------------
Start-Gate "G3 — Dev-token issuance (Issue 20)"
$anonTokResp = Invoke-Safe @{
    Uri = "$BASE/api/auth/dev-token"; Method = "POST"
    ContentType = "application/json"; Body = '{"userId":"anonymous"}'
}
Assert-Equal "POST /api/auth/dev-token" $anonTokResp.Status 200
$anonJson = $null; try { $anonJson = $anonTokResp.Body | ConvertFrom-Json } catch { }
Assert-True "Token body has .token" ($anonJson -and $anonJson.token -and $anonJson.token.Length -gt 100)
Assert-True "Token starts with 'eyJ'" ($anonJson -and $anonJson.token -and $anonJson.token.StartsWith("eyJ"))
Assert-Equal "Token body has userId=anonymous" ($anonJson.userId) "anonymous"
$anonToken = $anonJson.token
$anonHeaders = @{ Authorization = "Bearer $anonToken" }

$aliceJson = (Invoke-Safe @{
        Uri = "$BASE/api/auth/dev-token"; Method = "POST"
        ContentType = "application/json"; Body = '{"userId":"alice"}'
    }).Body | ConvertFrom-Json
Assert-Equal "Second user token issued" $aliceJson.userId "alice"
$aliceHeaders = @{ Authorization = "Bearer $($aliceJson.token)" }

# ---------------------------------------------------------
Start-Gate "G4 — Protected endpoints return 401 without token"
Assert-Equal "GET /api/profile  (no token)"     (Invoke-Safe @{ Uri = "$BASE/api/profile" }).Status 401
Assert-Equal "PUT /api/profile  (no token)"     (Invoke-Safe @{ Uri = "$BASE/api/profile"; Method = "PUT"; ContentType = "application/json"; Body = '{}' }).Status 401
Assert-Equal "GET /api/reports/x (no token)"    (Invoke-Safe @{ Uri = "$BASE/api/reports/x" }).Status 401
Assert-Equal "POST /api/reports  (no token)"    (Invoke-Safe @{ Uri = "$BASE/api/reports"; Method = "POST"; ContentType = "application/json"; Body = '{}' }).Status 401
Assert-Equal "GET /api/reports/x/pdf (no token)" (Invoke-Safe @{ Uri = "$BASE/api/reports/x/pdf" }).Status 401

# ---------------------------------------------------------
Start-Gate "G5 — Profile read (Issue 20 per-user)"
$profResp = Invoke-Safe @{ Uri = "$BASE/api/profile"; Headers = $anonHeaders }
Assert-Equal "GET /api/profile  (anonymous token)" $profResp.Status 200
$prof = $profResp.Body | ConvertFrom-Json
Assert-Equal "Profile.id"            $prof.id            "anonymous"
Assert-Equal "Profile.userId"        $prof.userId        "anonymous"
Assert-Equal "Profile.initialBalance" $prof.initialBalance 100000

# ---------------------------------------------------------
Start-Gate "G6 — Profile update round-trip"
$originalContribution = $prof.monthlyContribution
$putBody = @{
    initialBalance      = $prof.initialBalance
    monthlyContribution = [double]$prof.monthlyContribution + 1
    expectedReturnRate  = $prof.expectedReturnRate
    inflationRate       = $prof.inflationRate
} | ConvertTo-Json
Assert-Equal "PUT /api/profile" (Invoke-Safe @{
        Uri = "$BASE/api/profile"; Method = "PUT"
        Headers = $anonHeaders; ContentType = "application/json"; Body = $putBody
    }).Status 200
$after = (Invoke-Safe @{ Uri = "$BASE/api/profile"; Headers = $anonHeaders }).Body | ConvertFrom-Json
Assert-Equal "Profile update persisted" $after.monthlyContribution ([double]$originalContribution + 1)
$restoreBody = @{
    initialBalance      = $prof.initialBalance
    monthlyContribution = [double]$originalContribution
    expectedReturnRate  = $prof.expectedReturnRate
    inflationRate       = $prof.inflationRate
} | ConvertTo-Json
Invoke-Safe @{ Uri = "$BASE/api/profile"; Method = "PUT"; Headers = $anonHeaders; ContentType = "application/json"; Body = $restoreBody } | Out-Null

# ---------------------------------------------------------
Start-Gate "G7 — Report creation validation (Issue 12)"
$zeroBody = '{"reportType":"retirement-projection","projectionYears":0,"currency":"USD","includeScenarioAnalysis":true}'
$highBody = '{"reportType":"retirement-projection","projectionYears":101,"currency":"USD","includeScenarioAnalysis":true}'
$goodBody = '{"reportType":"retirement-projection","projectionYears":20,"currency":"USD","includeScenarioAnalysis":true}'
$r0 = Invoke-Safe @{ Uri = "$BASE/api/reports"; Method = "POST"; Headers = $anonHeaders; ContentType = "application/json"; Body = $zeroBody }
Assert-Equal "POST projectionYears=0 → 400" $r0.Status 400
$r0Json = $null; try { $r0Json = $r0.Body | ConvertFrom-Json } catch { }
Assert-True "400 body has code=invalid_projection_years" ($r0Json -and $r0Json.code -eq "invalid_projection_years")
$r101 = Invoke-Safe @{ Uri = "$BASE/api/reports"; Method = "POST"; Headers = $anonHeaders; ContentType = "application/json"; Body = $highBody }
Assert-Equal "POST projectionYears=101 → 400" $r101.Status 400

# ---------------------------------------------------------
Start-Gate "G8 — Report lifecycle end-to-end"
$createResp = Invoke-Safe @{ Uri = "$BASE/api/reports"; Method = "POST"; Headers = $anonHeaders; ContentType = "application/json"; Body = $goodBody }
Assert-Equal "POST /api/reports (valid)" $createResp.Status 200
$created = $createResp.Body | ConvertFrom-Json
$rid = $created.reportId
Assert-True "Response has reportId" ($rid -and $rid.Length -ge 16)
Assert-True "Response has correlationId" ($created.correlationId -and $created.correlationId.Length -ge 16)
Assert-Equal "Initial status is pending" $created.status "pending"
$deadline = (Get-Date).AddSeconds(40)
$finalStatus = $null; $report = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $r = Invoke-Safe @{ Uri = "$BASE/api/reports/$rid"; Headers = $anonHeaders }
    if ($r.Status -eq 200) {
        $report = $r.Body | ConvertFrom-Json
        if ($report.status -in @("completed", "needs_review", "failed")) { $finalStatus = $report.status; break }
    }
}
Assert-True "Workflow reached terminal status" ($finalStatus -ne $null) "status=$finalStatus after 40s"
Assert-Equal "Final status is 'completed'" $finalStatus "completed"

# ---------------------------------------------------------
Start-Gate "G9 — ReportStateDto shape (Issue 21)"
Assert-Equal "GET /api/reports/{id} → 200" (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid"; Headers = $anonHeaders }).Status 200
$stateFields = ($report | Get-Member -MemberType NoteProperty).Name | Sort-Object
$expectedFields = @("correlationId", "createdAtUtc", "downloadJsonUrl", "downloadPdfUrl", "reportId", "status") | Sort-Object
$fieldMatch = (($stateFields -join ",") -eq ($expectedFields -join ","))
Assert-True "State DTO has exactly {correlationId,createdAtUtc,downloadJsonUrl,downloadPdfUrl,reportId,status}" $fieldMatch "actual=$($stateFields -join ',')"
Assert-Equal "downloadJsonUrl populated for completed report" $report.downloadJsonUrl "/api/reports/$rid/json"
Assert-Equal "downloadPdfUrl populated for completed report"  $report.downloadPdfUrl  "/api/reports/$rid/pdf"

# ---------------------------------------------------------
Start-Gate "G10 — Detail endpoint (Issue 21)"
$detailResp = Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/detail"; Headers = $anonHeaders }
Assert-Equal "GET /api/reports/{id}/detail → 200" $detailResp.Status 200
$detail = $detailResp.Body | ConvertFrom-Json
Assert-True "detail has narrative"    ($detail.narrative -and $detail.narrative.Length -gt 0)
Assert-True "detail has keyRisks[]"   ($detail.keyRisks.Count -ge 1)
Assert-True "detail has citedSources" ($detail.citedSources -contains "src-001")
Assert-True "detail has assumptions"  ($detail.assumptions.Count -ge 1)
Assert-True "detail has confidenceScore" ($detail.confidenceScore -gt 0)

# ---------------------------------------------------------
Start-Gate "G11 — Report ownership (Issue 20)"
Assert-Equal "anonymous reads own report → 200" (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid"; Headers = $anonHeaders }).Status 200
Assert-Equal "alice reads anonymous's report → 404" (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid"; Headers = $aliceHeaders }).Status 404
Assert-Equal "alice reads detail → 404"   (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/detail"; Headers = $aliceHeaders }).Status 404
Assert-Equal "alice reads JSON → 404"     (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/json"; Headers = $aliceHeaders }).Status 404
Assert-Equal "alice reads PDF → 404"      (Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/pdf"; Headers = $aliceHeaders }).Status 404

# ---------------------------------------------------------
Start-Gate "G12 — Download endpoints (Issue 21)"
$jsonResp = Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/json"; Headers = $anonHeaders }
Assert-Equal "GET /json → 200" $jsonResp.Status 200
$jsonDoc = $null; try { $jsonDoc = $jsonResp.Body | ConvertFrom-Json } catch { }
Assert-True "JSON body is parseable" ($null -ne $jsonDoc)
Assert-True "JSON body has citedSources" ($jsonDoc.citedSources -contains "src-001")
$pdfResp = Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/pdf"; Headers = $anonHeaders }
Assert-Equal "GET /pdf → 200" $pdfResp.Status 200
$ctHeader = $pdfResp.Headers["Content-Type"]
Assert-True "PDF content-type is application/pdf" ($ctHeader -and ($ctHeader -join ";") -match "application/pdf") "actual=$ctHeader"

# ---------------------------------------------------------
Start-Gate "G13 — Correlation ID propagation"
$corrResp = Invoke-WebRequest "$BASE/healthz" -UseBasicParsing
$corrHeader = $corrResp.Headers["X-Correlation-Id"]
Assert-True "Response carries X-Correlation-Id header" ($corrHeader -and ($corrHeader -join "").Length -ge 16) "actual=$corrHeader"
$testCorr = [guid]::NewGuid().ToString("N")
$echoResp = Invoke-WebRequest "$BASE/healthz" -Headers @{ "X-Correlation-Id" = $testCorr } -UseBasicParsing
Assert-Equal "Provided X-Correlation-Id is echoed" ($echoResp.Headers["X-Correlation-Id"] -join "") $testCorr
Assert-True "Report body has correlationId" ($report.correlationId -and $report.correlationId.Length -ge 16)

# ---------------------------------------------------------
Start-Gate "G14 — Global exception handler (Issue 18)"
try { Invoke-RestMethod "$BASE/throw" } catch {
    $err = $_              # error record — has ErrorDetails
    $p = $err.Exception    # exception — has Response
    Assert-Equal "GET /throw → 500" ([int]$p.Response.StatusCode) 500
    Assert-Equal "Content-Type is application/problem+json" `
    ($p.Response.Content.Headers.ContentType.ToString() -replace ";.*$", "") `
        "application/problem+json"
    $body = $err.ErrorDetails.Message | ConvertFrom-Json
    Assert-True "Body has correlationId" ($body.correlationId -and $body.correlationId.Length -gt 0)
    Assert-True "Body does NOT leak exception message" ($body.detail -notmatch "Deliberate test exception")
}

# ---------------------------------------------------------
Start-Gate "G15 — MarkCompletedAsync idempotency (Issue 26)"
# Covered by G8's "Final status is completed" — PatchItemAsync produces
# the same externally visible status. Additional assertions verifying
# no-read-path are in the unit test suite, not here.
# =========================================================

# ---------------------------------------------------------
Start-Gate "G16 — Correlation ID propagation .NET to Python (Issue 19)"
$corr = "gated-corr-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$body = '{"reportType":"retirement-projection","projectionYears":20,"currency":"USD","includeScenarioAnalysis":true}'
$r = Invoke-Safe @{
    Uri = "$BASE/api/reports"; Method = "POST"
    Headers = @{ Authorization = "Bearer $anonToken"; "X-Correlation-Id" = $corr }
    ContentType = "application/json"; Body = $body
}
Assert-Equal "POST with explicit correlationId → 200" $r.Status 200

Start-Sleep -Seconds 8

$netLogs = @(docker compose logs --tail 300 api 2>&1 | Select-String $corr)
$pyLogs = @(docker compose logs --tail 300 ai  2>&1 | Select-String $corr)
Assert-True ".NET log contains correlation ID"  ($netLogs.Count -ge 1) "matches=0"
Assert-True "Python log contains correlation ID" ($pyLogs.Count -ge 1) "matches=0"

if ($pyLogs.Count -ge 1) {
    $firstPy = ($pyLogs[0].Line -replace "^financialai-ai\s*\|\s*", "").Trim()
    $parsed = $null
    try { $parsed = $firstPy | ConvertFrom-Json } catch { }
    Assert-True "Python log line is valid JSON" ($null -ne $parsed)
    if ($parsed) {
        Assert-Equal "Python JSON has correlation_id" $parsed.correlation_id $corr
    }
}

# ---------------------------------------------------------
Start-Gate "G17 — AI service error handling (Issue 15)"
# Zero runtime assertions — the retry and typed-exception path fires only on
# non-2xx from the AI service. With a healthy stack, /v1/analyze returns 200
# for every request, so this path cannot be exercised in the gated suite
# without stopping the ai container mid-test.
#
# Covered by unit tests:
#   tests/FinancialAI.UnitTests/AiServiceClientTests.cs (3 tests)
#     - AnalyzeAsync_On502_CapturesBodyAndCorrelation
#     - AnalyzeAsync_On500WithStringDetail_StillThrowsTypedException
#     - AnalyzeAsync_OnNonJsonErrorBody_StillThrowsTypedException
# ---------------------------------------------------------

Start-Gate "G18 — PDF endpoint renders on demand (Issue 14)"
$pdfReq = Invoke-Safe @{ Uri = "$BASE/api/reports/$rid/pdf"; Headers = $anonHeaders }
Assert-Equal "GET /pdf → 200 after executor change" $pdfReq.Status 200
$ctHeader2 = $pdfReq.Headers["Content-Type"]
Assert-True "PDF content-type intact" ($ctHeader2 -and ($ctHeader2 -join ";") -match "application/pdf") "actual=$ctHeader2"

# ---------------------------------------------------------
# Gate 19 — CORS preflight
Start-Gate "G19 — CORS policy (Milestone 8a)"
$corsHeaders = @{
    "Origin" = "http://localhost:3000"
    "Access-Control-Request-Method" = "GET"
    "Access-Control-Request-Headers" = "authorization"
}
$corsResp = $null
try {
    $corsResp = Invoke-WebRequest -Method Options "$BASE/api/profile" `
        -Headers $corsHeaders -UseBasicParsing -ErrorAction Stop
} catch {
    $corsResp = $_.Exception.Response
}

if ($corsResp) {
    Assert-Equal "OPTIONS /api/profile from allowed origin → 204" ([int]$corsResp.StatusCode) 204
    Assert-Equal "Allow-Origin echoes request origin" `
        ($corsResp.Headers["Access-Control-Allow-Origin"] -join "") "http://localhost:3000"
    # Access-Control-Expose-Headers appears on the actual (non-preflight)
    # response, not the OPTIONS preflight. Verify on a real authenticated GET.
    $actualGet = Invoke-WebRequest -Method GET "$BASE/api/profile" `
        -Headers @{ "Origin" = "http://localhost:3000"; "Authorization" = "Bearer $anonToken" } `
        -UseBasicParsing -ErrorAction SilentlyContinue
    $exposeHeader = if ($actualGet) { ($actualGet.Headers["Access-Control-Expose-Headers"] -join ";") } else { "" }
    Assert-True "Exposes X-Correlation-Id on actual GET" ($exposeHeader -match "X-Correlation-Id") "actual=$exposeHeader"
} else {
    Assert-True "OPTIONS preflight reachable" $false "no response from API"
    Assert-True "Allow-Origin echoes request origin" $false "skipped"
    Assert-True "Exposes X-Correlation-Id header" $false "skipped"
}

$evilResp = $null
try {
    $evilResp = Invoke-WebRequest -Method Options "$BASE/api/profile" `
        -Headers @{ "Origin" = "http://evil.example"; "Access-Control-Request-Method" = "GET" } `
        -UseBasicParsing -ErrorAction Stop
} catch {
    $evilResp = $_.Exception.Response
}
$evilOrigin = if ($evilResp) { $evilResp.Headers["Access-Control-Allow-Origin"] -join "" } else { "" }
Assert-True "Disallowed origin gets no Allow-Origin header" ([string]::IsNullOrEmpty($evilOrigin)) "actual=$evilOrigin"
# ---------------------------------------------------------

Write-Host "`n===========================================" -ForegroundColor Cyan
Write-Host "  RESULTS" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan

$grouped = $script:results | Group-Object Gate
$totalPass = 0; $totalFail = 0
foreach ($g in $grouped) {
    $p = ($g.Group | Where-Object Status -eq "PASS").Count
    $f = ($g.Group | Where-Object Status -eq "FAIL").Count
    $totalPass += $p; $totalFail += $f
    $color = if ($f -eq 0) { "Green" } else { "Red" }
    Write-Host ("{0,-60} {1,3} pass, {2,3} fail" -f $g.Name, $p, $f) -ForegroundColor $color
}
Write-Host "-------------------------------------------" -ForegroundColor Cyan
$overall = if ($totalFail -eq 0) { "ALL PASS" } else { "$totalFail FAILURE(S)" }
$overallColor = if ($totalFail -eq 0) { "Green" } else { "Red" }
Write-Host ("TOTAL: {0} pass, {1} fail — {2}" -f $totalPass, $totalFail, $overall) -ForegroundColor $overallColor

if ($totalFail -gt 0) {
    Write-Host "`nFailed checks:" -ForegroundColor Red
    $script:results | Where-Object Status -eq "FAIL" | Format-Table Gate, Test, Expected, Actual -AutoSize
}

$script:results | ConvertTo-Json -Depth 4 | Set-Content "api-test-results.json" -Encoding UTF8
Write-Host "`nFull results saved to api-test-results.json" -ForegroundColor DarkGray

