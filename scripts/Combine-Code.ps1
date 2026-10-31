# ---------- Redaction helpers ----------
function Test-ShouldRedact {
    param([string]$FileName)
    if ($FileName -match '^appsettings(\..*)?\.json$') { return $true }
    if ($FileName -match '^launchSettings\.json$') { return $true }
    if ($FileName -match '\.secrets\.json$') { return $true }
    if ($FileName -eq '.env') { return $true }
    if ($FileName -match '^\.env\..+') { return $true }
    return $false
}

function Redact-JsonContent {
    param(
        [string]$Content,
        [string]$Replacement = '***REDACTED***'
    )
    if ([string]::IsNullOrEmpty($Content)) { return $Content }

    # Sensitive key fragments (regex, case-insensitive)
    $alt = '(?:connection.?string|password|secret|api[_-]?key|account[_-]?key|access[_-]?key|primary[_-]?key|secondary[_-]?key|shared[_-]?key|shared[_-]?access[_-]?key|subscription[_-]?key|client[_-]?secret|private[_-]?key|shared[_-]?access[_-]?signature|sas[_-]?token|token|signature)'

    # Step 1: generic sensitive key/value pairs
    $keyPattern = [regex]::new('("[^"]*' + $alt + '[^"]*"\s*:\s*)"(?:[^"\\]|\\.)*"', 'IgnoreCase')
    $Content = $keyPattern.Replace($Content, ('${1}"' + $Replacement + '"'))

    # Step 2: redact every string value inside a "ConnectionStrings" object
    $csPattern = [regex]::new('("ConnectionStrings"\s*:\s*\{)([^{}]*)(\})', 'IgnoreCase')
    $evaluator = {
        param($m)
        $inner = [regex]::Replace(
            $m.Groups[2].Value,
            '("[^"]+"\s*:\s*)"(?:[^"\\]|\\.)*"',
            ('${1}"' + $Replacement + '"')
        )
        return $m.Groups[1].Value + $inner + $m.Groups[3].Value
    }.GetNewClosure()
    $Content = $csPattern.Replace($Content, $evaluator)

    return $Content
}

function Redact-EnvContent {
    param(
        [string]$Content,
        [string]$Replacement = '***REDACTED***'
    )
    if ([string]::IsNullOrEmpty($Content)) { return $Content }
    $pattern = '(?im)^(\s*(?:[A-Za-z0-9_]*(?:KEY|SECRET|PASSWORD|TOKEN|CONNECTION|API|SAS|ACCOUNT|SIGNATURE)[A-Za-z0-9_]*)\s*=\s*).*$'
    return [regex]::Replace($Content, $pattern, ('${1}' + $Replacement))
}

# ---------- Main combiner ----------
function Combine-Code {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$Output,
        [string[]]$Extensions = @(),
        [string[]]$IncludeFileNames = @(),
        [string[]]$ExcludeDirs = @(),
        [int]$MaxFileSizeKB = 400,
        [int]$SplitEveryKB = 0,
        [switch]$NoRedact,
        [string]$RedactReplacement = '***REDACTED***'
    )

    if (-not (Test-Path -LiteralPath $SourceRoot)) { throw "Source root not found: $SourceRoot" }
    $rootFull = (Resolve-Path -LiteralPath $SourceRoot).Path.TrimEnd('\', '/')

    $excludeSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($d in $ExcludeDirs) { [void]$excludeSet.Add($d) }
    $extSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($e in $Extensions) { [void]$extSet.Add($e.ToLowerInvariant()) }
    $nameSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $IncludeFileNames) { [void]$nameSet.Add($n) }

    $selected = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $skipped = New-Object System.Collections.Generic.List[string]

    foreach ($f in (Get-ChildItem -LiteralPath $rootFull -Recurse -File -ErrorAction SilentlyContinue)) {
        $rel = $f.FullName.Substring($rootFull.Length).TrimStart('\', '/')
        $segs = $rel -split '[\\/]'
        $excluded = $false
        foreach ($s in $segs) { if ($excludeSet.Contains($s)) { $excluded = $true; break } }
        if ($excluded) { continue }

        $isMatch = $nameSet.Contains($f.Name) -or
        ($f.Extension -and $extSet.Contains($f.Extension.ToLowerInvariant()))
        if (-not $isMatch) { continue }

        if ($f.Length -gt $MaxFileSizeKB * 1024) { $skipped.Add($rel); continue }
        $selected.Add($f)
    }

    $sorted = $selected | Sort-Object { $_.FullName.Substring($rootFull.Length) }
    $blocks = New-Object System.Collections.Generic.List[string]
    $manifest = New-Object System.Collections.Generic.List[string]
    $totalLines = 0
    $redactedCount = 0

    foreach ($f in $sorted) {
        $rel = $f.FullName.Substring($rootFull.Length).TrimStart('\', '/')
        $content = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 -ErrorAction Stop
        if ($null -eq $content) { $content = '' }

        if (-not $NoRedact -and (Test-ShouldRedact -FileName $f.Name)) {
            if ($f.Extension -ieq '.json') {
                $content = Redact-JsonContent -Content $content -Replacement $RedactReplacement
            }
            else {
                $content = Redact-EnvContent  -Content $content -Replacement $RedactReplacement
            }
            $redactedCount++
        }

        $lines = ($content -split "`n").Count
        $totalLines += $lines
        $sep = ('=' * 18)
        $blocks.Add("`n$sep FILE: $rel $sep`n$content`n")
        $manifest.Add(("{0,8} lines  {1}" -f $lines, $rel))
    }

    $enc = New-Object System.Text.UTF8Encoding($false)
    $full = [IO.Path]::GetFullPath($Output)
    $dir = [IO.Path]::GetDirectoryName($full)

    if ($SplitEveryKB -le 0) {
        [IO.File]::WriteAllText($full, ($blocks -join ''), $enc)
        Write-Host ("Wrote {0}" -f $full)
    }
    else {
        $parts = New-Object System.Collections.Generic.List[string]
        $cur = New-Object System.Text.StringBuilder
        $curLen = 0; $limit = $SplitEveryKB * 1024
        foreach ($b in $blocks) {
            $bl = [Text.Encoding]::UTF8.GetByteCount($b)
            if ($cur.Length -gt 0 -and ($curLen + $bl) -gt $limit) {
                $parts.Add($cur.ToString()); [void]$cur.Clear(); $curLen = 0
            }
            [void]$cur.Append($b); $curLen += $bl
        }
        if ($cur.Length -gt 0) { $parts.Add($cur.ToString()) }

        $base = [IO.Path]::GetFileNameWithoutExtension($full)
        for ($i = 0; $i -lt $parts.Count; $i++) {
            $p = Join-Path $dir ("{0}.part{1:D2}.txt" -f $base, ($i + 1))
            [IO.File]::WriteAllText($p, $parts[$i], $enc)
            Write-Host ("Wrote {0}  ({1:N0} KB)" -f $p, ($parts[$i].Length / 1KB))
        }
    }

    $manifestPath = Join-Path $dir ([IO.Path]::GetFileNameWithoutExtension($full) + '.manifest.txt')
    [IO.File]::WriteAllText($manifestPath, (($manifest -join "`n") + "`n"), $enc)

    Write-Host ''
    Write-Host ("Files included : {0}" -f $blocks.Count)
    Write-Host ("Total lines    : {0:N0}" -f $totalLines)
    if ($redactedCount -gt 0) {
        Write-Host ("Files redacted : {0} (appsettings/launchSettings/.env)" -f $redactedCount)
    }
    if ($skipped.Count -gt 0) {
        Write-Host ("Skipped (>${MaxFileSizeKB}KB): {0}" -f ($skipped -join ', '))
    }
    Write-Host ("Manifest       : {0}" -f $manifestPath)
}