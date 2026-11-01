#Requires -Version 7.0
<#
.SYNOPSIS
  Generates combined-csharp.txt, combined-python.txt, combined-web.txt
  from the FinancialAgenticAI repository.

.DESCRIPTION
  Run from the repo root (where src/, services/ai/, and web/ exist).
  Requires Combine-Code.ps1 in the same directory.

.PARAMETER SplitEveryKB
  If > 0, splits each output into .partNN.txt files instead of one large file.

.PARAMETER IncludeTests
  Also produce combined-tests.txt from tests/.
#>
[CmdletBinding()]
param(
    [int]$SplitEveryKB = 0,
    [switch]$IncludeTests
)

$ErrorActionPreference = 'Stop'

# ─── Sanity: we must be in the repo root ─────────────────────────────────
if (-not (Test-Path '.\src') -or -not (Test-Path '.\services\ai') -or -not (Test-Path '.\web')) {
    Write-Host "This script must be run from the repo root." -ForegroundColor Red
    Write-Host "  Expected: .\src, .\services\ai, .\web all present." -ForegroundColor Yellow
    Write-Host ("  Current directory: {0}" -f (Get-Location)) -ForegroundColor Yellow
    exit 1
}

# ─── Load helper functions from the same directory as this script ────────
if (-not (Get-Command Combine-Code -ErrorAction SilentlyContinue)) {
    $helper = Join-Path $PSScriptRoot 'Combine-Code.ps1'
    if (-not (Test-Path $helper)) {
        Write-Host "Combine-Code.ps1 not found at: $helper" -ForegroundColor Red
        exit 1
    }
    . $helper
}

$splitArg = @{}
if ($SplitEveryKB -gt 0) { $splitArg['SplitEveryKB'] = $SplitEveryKB }

# ─── C# ──────────────────────────────────────────────────────────────────
Write-Host "`n=== C# ===" -ForegroundColor Cyan
Combine-Code @splitArg `
    -SourceRoot '.\src' `
    -Output '.\combined-csharp.txt' `
    -Extensions '.cs', '.csproj', '.json', '.yaml', '.yml' `
    -IncludeFileNames 'Dockerfile' `
    -ExcludeDirs 'bin', 'obj', '.vs', '.vscode', '.idea', 'TestResults', 'Migrations' `
    -MaxFileSizeKB 300

# ─── Python ──────────────────────────────────────────────────────────────
Write-Host "`n=== Python ===" -ForegroundColor Cyan
Combine-Code @splitArg `
    -SourceRoot '.\services\ai' `
    -Output '.\combined-python.txt' `
    -Extensions '.py', '.toml', '.txt', '.yml', '.yaml', '.ini', '.cfg' `
    -IncludeFileNames 'Dockerfile' `
    -ExcludeDirs '__pycache__', '.venv', 'venv', '.pytest_cache', '.mypy_cache', '.ruff_cache', 'dist', 'build', 'node_modules' `
    -MaxFileSizeKB 250

# ─── TypeScript ──────────────────────────────────────────────────────────
Write-Host "`n=== TypeScript ===" -ForegroundColor Cyan
Combine-Code @splitArg `
    -SourceRoot '.\web' `
    -Output '.\combined-web.txt' `
    -Extensions '.ts', '.tsx', '.js', '.jsx', '.json', '.css' `
    -ExcludeDirs 'node_modules', '.next', 'dist', 'build', 'coverage', '.vercel', '.turbo', 'public' `
    -MaxFileSizeKB 250

# ─── Optional: tests ─────────────────────────────────────────────────────
if ($IncludeTests) {
    Write-Host "`n=== C# tests ===" -ForegroundColor Cyan
    Combine-Code @splitArg `
        -SourceRoot '.\tests' `
        -Output '.\combined-tests.txt' `
        -Extensions '.cs', '.csproj', '.json' `
        -ExcludeDirs 'bin', 'obj', 'TestResults' `
        -MaxFileSizeKB 300
}

# ─── Summary ─────────────────────────────────────────────────────────────
Write-Host "`n═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "All outputs written to: $(Get-Location)" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Get-ChildItem -Path .\combined-*.txt, .\combined-*.manifest.txt -ErrorAction SilentlyContinue |
Select-Object Name, @{N = 'KB'; E = { [math]::Round($_.Length / 1KB, 1) } } |
Sort-Object Name |
Format-Table -AutoSize