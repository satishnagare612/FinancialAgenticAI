#Requires -Version 5.1
<#
.SYNOPSIS
  Generates CONTEXT.md — a self-contained session handoff document.

.DESCRIPTION
  Merges README.md, the combined source bundles (combined-csharp.txt,
  combined-python.txt, combined-web.txt), their manifests, and a
  hand-curated SESSION-NOTES.md into one CONTEXT.md.

  Section layout of the output:
     1. Project overview            (from README)
     2. Architecture                (from README)
     3. Tech stack                  (from README)
     4. File inventory              (from manifests)
     5. HTTP API surface            (from C# bundle)
     6. Configuration (redacted)    (from C# bundle)
     7. Production configuration    (from README)
     8. Security notes              (from README)
     9. Session findings            (from SESSION-NOTES.md, verbatim)
    10. Source checksums            (SHA-256 of every input)
    11. How to regenerate

.PARAMETER ScriptVersion
  Free-form version of this generator (e.g. "1.0.0", "1.1.0"). Embedded
  in the output header so downstream readers can tell which version of
  the generator produced the file.

.EXAMPLE
  .\New-Context.ps1

.EXAMPLE
  .\New-Context.ps1 -ScriptVersion 1.1.0 -SessionNotes .\notes\SESSION.md
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = ".",
    [string]$ReadmePath = "README.md",
    [string]$CombinedCSharp = "combined-csharp.txt",
    [string]$CombinedPython = "combined-python.txt",
    [string]$CombinedWeb = "combined-web.txt",
    [string]$ManifestCSharp = "combined-csharp.manifest.txt",
    [string]$ManifestPython = "combined-python.manifest.txt",
    [string]$ManifestWeb = "combined-web.manifest.txt",
    [string]$SessionNotes = "SESSION-NOTES.md",
    [string]$OutputPath = "CONTEXT.md",
    [string]$ScriptVersion = "1.0.0"
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepoRoot).Path

function Resolve-Relative {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($root, $Path))
}

function Read-IfExists {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) { return Get-Content -LiteralPath $Path -Raw }
    return ""
}

function Get-Sha256 {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "(missing)" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-BundleSections {
    param([string]$Content)
    $sections = [ordered]@{}
    if ([string]::IsNullOrWhiteSpace($Content)) { return $sections }
    $pattern = '(?m)^=+ FILE: (.+?) =+[ \t]*\r?$'
    $matches = [regex]::Matches($Content, $pattern)
    for ($i = 0; $i -lt $matches.Count; $i++) {
        $name = $matches[$i].Groups[1].Value.Trim()
        $start = $matches[$i].Index + $matches[$i].Length
        if ($i + 1 -lt $matches.Count) { $end = $matches[$i + 1].Index } else { $end = $Content.Length }
        $sections[$name] = $Content.Substring($start, $end - $start).Trim()
    }
    return $sections
}

function Get-ReadmeSection {
    param([string]$Content, [string]$Heading)
    if ([string]::IsNullOrWhiteSpace($Content)) { return "" }
    $escaped = [regex]::Escape($Heading)
    $pattern = "(?ms)^##\s+$escaped[ \t]*\r?\n(.*?)(?=^##\s|\z)"
    $m = [regex]::Match($Content, $pattern)
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ""
}

function Extract-ApiSurface {
    param([string]$CombinedCSharp)
    $endpoints = @()
    $sections = Get-BundleSections -Content $CombinedCSharp
    foreach ($file in $sections.Keys) {
        if ($file -notmatch '(?i)Endpoints.*Endpoints\.cs$') { continue }
        $content = $sections[$file]
        $groupMatch = [regex]::Match($content, 'MapGroup\("([^"]+)"')
        $basePath = if ($groupMatch.Success) { $groupMatch.Groups[1].Value } else { "" }
        $mapMatches = [regex]::Matches($content, '\.Map(Get|Post|Put|Delete|Patch)\("([^"]*)"')
        foreach ($m in $mapMatches) {
            $verb = $m.Groups[1].Value.ToUpper()
            $path = $m.Groups[2].Value
            $full = if ($basePath -and $path) { "$basePath$path" }
            elseif ($basePath) { $basePath }
            else { $path }
            $endpoints += [PSCustomObject]@{ Verb = $verb; Path = $full; File = $file }
        }
    }
    return $endpoints
}

function Extract-ConfigKeys {
    param([string]$CombinedCSharp)
    $rows = @()
    $sections = Get-BundleSections -Content $CombinedCSharp
    foreach ($file in $sections.Keys) {
        if ($file -notmatch '(?i)appsettings(\..+)?\.json$') { continue }
        $json = $sections[$file]
        $obj = $null
        try { $obj = $json | ConvertFrom-Json } catch { continue }
        if ($null -eq $obj) { continue }
        foreach ($prop in $obj.PSObject.Properties) {
            if ($prop.Value -is [PSCustomObject]) {
                $childKeys = (($prop.Value.PSObject.Properties | ForEach-Object { "``$($_.Name)``" }) -join ", ")
                $rows += [PSCustomObject]@{ File = $file; Section = $prop.Name; Keys = $childKeys }
            }
            else {
                $rows += [PSCustomObject]@{ File = $file; Section = $prop.Name; Keys = "(scalar)" }
            }
        }
    }
    return $rows
}

# ---------- Resolve inputs ----------
$readmeFull = Resolve-Relative $ReadmePath
$csharpFull = Resolve-Relative $CombinedCSharp
$pythonFull = Resolve-Relative $CombinedPython
$webFull = Resolve-Relative $CombinedWeb
$manCsharpFull = Resolve-Relative $ManifestCSharp
$manPythonFull = Resolve-Relative $ManifestPython
$manWebFull = Resolve-Relative $ManifestWeb
$notesFull = Resolve-Relative $SessionNotes
$outFull = Resolve-Relative $OutputPath

if (-not (Test-Path -LiteralPath $notesFull)) {
    $template = @"
# SESSION-NOTES

> Hand-curated. ``New-Context.ps1`` inlines this file verbatim under
> section 9 of ``CONTEXT.md``. Update between regenerations.

## Current Objective

_(one paragraph)_

## Findings

### F-001 — (title)

- **Severity**: (critical / high / medium / low)
- **Status**: (open / investigating / fixed / wontfix)
- **Symptom**:
- **Root Cause**:
- **Fix**:
- **Verification**:

## Decisions

- _(decision, with the date and the reason)_

## Open Questions

- _(question that blocks progress or needs stakeholder input)_

## Next Steps

1. _(ordered list of the next actions)_

## Changelog

- $(Get-Date -Format 'yyyy-MM-dd'): notes file created by New-Context.ps1
"@
    [System.IO.File]::WriteAllText($notesFull, $template, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Created template: $notesFull"
}

# ---------- Read inputs ----------
$readme = Read-IfExists $readmeFull
$csharp = Read-IfExists $csharpFull
$python = Read-IfExists $pythonFull
$web = Read-IfExists $webFull
$notes = Read-IfExists $notesFull

$manCsharp = Read-IfExists $manCsharpFull
$manPython = Read-IfExists $manPythonFull
$manWeb = Read-IfExists $manWebFull

# ---------- Extract ----------
$overviewLines = @()
if ($readme) {
    $lines = $readme -split "`n"
    $section = @()
    $inCode = $false
    foreach ($l in $lines) {
        if ($l -match '^##') { if ($section.Count -gt 0) { break } else { continue } }
        if ($l -match '^```') { $inCode = -not $inCode; continue }
        if (-not $inCode -and $l.Trim()) { $section += $l }
        if ($section.Count -ge 3) { break }
    }
    $overviewLines = $section
}
$overview = ($overviewLines -join "`n").Trim()
$architecture = Get-ReadmeSection -Content $readme -Heading "Architecture"
$techStack = Get-ReadmeSection -Content $readme -Heading "Tech stack"
$prodConfig = Get-ReadmeSection -Content $readme -Heading "Production configuration"
$security = Get-ReadmeSection -Content $readme -Heading "Security notes"

$apiSurface = Extract-ApiSurface -CombinedCSharp $csharp
$configKeys = Extract-ConfigKeys -CombinedCSharp $csharp

# ---------- Checksums ----------
$checksums = [ordered]@{
    "combined-csharp.txt"          = Get-Sha256 $csharpFull
    "combined-python.txt"          = Get-Sha256 $pythonFull
    "combined-web.txt"             = Get-Sha256 $webFull
    "combined-csharp.manifest.txt" = Get-Sha256 $manCsharpFull
    "combined-python.manifest.txt" = Get-Sha256 $manPythonFull
    "combined-web.manifest.txt"    = Get-Sha256 $manWebFull
    "README.md"                    = Get-Sha256 $readmeFull
    "SESSION-NOTES.md"             = Get-Sha256 $notesFull
}

# ---------- Compose ----------
$sb = [System.Text.StringBuilder]::new()

[void]$sb.AppendLine("# FinancialAI — Session Context")
[void]$sb.AppendLine()
[void]$sb.AppendLine("> **Generator** ``New-Context.ps1`` v$ScriptVersion  ")
[void]$sb.AppendLine("> **Generated at** $(Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')  ")
[void]$sb.AppendLine("> **Repo root** ``$root``")
[void]$sb.AppendLine()
[void]$sb.AppendLine("This file is **regenerable**. The durable artifact is ``SESSION-NOTES.md`` — edit that, then re-run the generator. See section 11.")
[void]$sb.AppendLine()

if ($overview) {
    [void]$sb.AppendLine("## 1. Project Overview")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($overview)
    [void]$sb.AppendLine()
}

if ($architecture) {
    [void]$sb.AppendLine("## 2. Architecture")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine($architecture)
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine()
}

if ($techStack) {
    [void]$sb.AppendLine("## 3. Tech Stack")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($techStack)
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine("## 4. File Inventory")
[void]$sb.AppendLine()
[void]$sb.AppendLine("### C# (API + Application + Domain + Infrastructure)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($manCsharp.TrimEnd())
[void]$sb.AppendLine('```')
[void]$sb.AppendLine()
[void]$sb.AppendLine("### Python (AI service)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($manPython.TrimEnd())
[void]$sb.AppendLine('```')
[void]$sb.AppendLine()
[void]$sb.AppendLine("### TypeScript (Next.js web)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($manWeb.TrimEnd())
[void]$sb.AppendLine('```')
[void]$sb.AppendLine()

if ($apiSurface.Count -gt 0) {
    [void]$sb.AppendLine("## 5. HTTP API Surface")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| Verb | Path | Source file |")
    [void]$sb.AppendLine("|------|------|-------------|")
    foreach ($e in $apiSurface) {
        [void]$sb.AppendLine("| $($e.Verb) | ``$($e.Path)`` | ``$($e.File)`` |")
    }
    [void]$sb.AppendLine()
}

if ($configKeys.Count -gt 0) {
    [void]$sb.AppendLine("## 6. Configuration (values redacted)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| File | Section | Keys |")
    [void]$sb.AppendLine("|------|---------|------|")
    foreach ($c in $configKeys) {
        [void]$sb.AppendLine("| ``$($c.File)`` | ``$($c.Section)`` | $($c.Keys) |")
    }
    [void]$sb.AppendLine()
}

if ($prodConfig) {
    [void]$sb.AppendLine("## 7. Production Configuration")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($prodConfig)
    [void]$sb.AppendLine()
}

if ($security) {
    [void]$sb.AppendLine("## 8. Security Notes")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($security)
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine("## 9. Session Findings")
[void]$sb.AppendLine()
if ($notes) {
    [void]$sb.AppendLine($notes.TrimEnd())
}
else {
    [void]$sb.AppendLine("_SESSION-NOTES.md is missing or empty._")
}
[void]$sb.AppendLine()

[void]$sb.AppendLine("## 10. Source Checksums")
[void]$sb.AppendLine()
[void]$sb.AppendLine("If any of these change, re-run the generator to refresh ``CONTEXT.md``.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("| File | SHA-256 |")
[void]$sb.AppendLine("|------|---------|")
foreach ($k in $checksums.Keys) {
    [void]$sb.AppendLine("| ``$k`` | ``$($checksums[$k])`` |")
}
[void]$sb.AppendLine()

[void]$sb.AppendLine("## 11. How to Regenerate")
[void]$sb.AppendLine()
[void]$sb.AppendLine('```powershell')
[void]$sb.AppendLine("# From the repo root:")
[void]$sb.AppendLine(".\New-Context.ps1 -ScriptVersion $ScriptVersion")
[void]$sb.AppendLine()
[void]$sb.AppendLine("# Or with explicit paths:")
[void]$sb.AppendLine(".\New-Context.ps1 ``")
[void]$sb.AppendLine("    -RepoRoot . ``")
[void]$sb.AppendLine("    -SessionNotes .\SESSION-NOTES.md ``")
[void]$sb.AppendLine("    -OutputPath .\CONTEXT.md ``")
[void]$sb.AppendLine("    -ScriptVersion $ScriptVersion")
[void]$sb.AppendLine('```')
[void]$sb.AppendLine()
[void]$sb.AppendLine("**Workflow:**")
[void]$sb.AppendLine()
[void]$sb.AppendLine("1. Update ``SESSION-NOTES.md`` with new findings, decisions, next steps.")
[void]$sb.AppendLine("2. Run the script. ``CONTEXT.md`` is overwritten.")
[void]$sb.AppendLine("3. Use ``CONTEXT.md`` as the handoff artifact after a chat session ends.")
[void]$sb.AppendLine("4. Bump ``-ScriptVersion`` only when the **script logic** changes, not when only the content changes.")
[void]$sb.AppendLine()

# ---------- Write ----------
[System.IO.File]::WriteAllText($outFull, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host ("Wrote {0}" -f $outFull)
Write-Host ("Generator : New-Context.ps1 v{0}" -f $ScriptVersion)
Write-Host ("Size      : {0:N0} bytes" -f (Get-Item -LiteralPath $outFull).Length)
Write-Host ""
Write-Host "Input checksums:"
foreach ($k in $checksums.Keys) { Write-Host ("  {0,-32} {1}" -f $k, $checksums[$k]) }