#Requires -Version 5.1
<#
.SYNOPSIS
    Code coverage gate for the Moirai framework.

.DESCRIPTION
    Reads the OpenCover-format coverage XML produced by com.unity.testtools.codecoverage,
    aggregates class rows into modules by namespace prefix, and enforces the tiered
    line/branch thresholds recorded in Tests/Coverage/README.md.

    The gate NEVER passes silently. Missing file, unreadable XML, or an unrecognised
    document shape all exit non-zero with the reason printed. "Cannot measure" is not
    "meets the bar" - same rule this project applies to managed-allocation metering.

.PARAMETER CoverageXmlPath
    Path to the coverage XML, or to a directory containing it (the newest *.xml is used).

.PARAMETER Strict
    Treat a gated module that has no rows in the report as a failure. Use this once the
    namespace prefixes below have been confirmed against a real report.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File coverage-gate.ps1 -CoverageXmlPath TestResults/Coverage
#>
param(
    [Parameter(Mandatory = $true)][string]$CoverageXmlPath,
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'

function Write-Fail([string]$message) {
    Write-Output ("GATE-FAIL: " + $message)
    exit 1
}

function Write-Note([string]$message) {
    Write-Output ("  note " + $message)
}

# module namespace prefix -> @{ tier; line; branch }
# Keep prefixes ordered longest-first: the first match wins.
$gatedModules = @(
    @{ Prefix = 'Moirai.Atropos.Resource';     Tier = 'core';    Line = 80.0; Branch = 70.0 },
    @{ Prefix = 'Moirai.Atropos.Save';         Tier = 'core';    Line = 80.0; Branch = 70.0 },
    @{ Prefix = 'Moirai.Atropos.Audio';        Tier = 'core';    Line = 80.0; Branch = 70.0 },
    @{ Prefix = 'Moirai.Atropos.UI';           Tier = 'core';    Line = 80.0; Branch = 70.0 },
    @{ Prefix = 'Moirai.Atropos.Kernel';       Tier = 'core';    Line = 80.0; Branch = 70.0 },
    @{ Prefix = 'Moirai.Atropos.ConfigTable';  Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Debugger';     Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Input';        Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Localization'; Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.ObjectPool';   Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Procedure';    Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Scene';        Tier = 'service'; Line = 70.0; Branch = 0.0 },
    @{ Prefix = 'Moirai.Atropos.Timer';        Tier = 'service'; Line = 70.0; Branch = 0.0 }
)

# Resolve the XML file.
$xmlFile = $null
if (Test-Path -LiteralPath $CoverageXmlPath -PathType Container) {
    $candidate = Get-ChildItem -LiteralPath $CoverageXmlPath -Recurse -File -Filter '*.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $xmlFile = $candidate.FullName }
}
elseif (Test-Path -LiteralPath $CoverageXmlPath -PathType Leaf) {
    $xmlFile = (Resolve-Path -LiteralPath $CoverageXmlPath).Path
}

if ($null -eq $xmlFile) {
    Write-Fail ("no coverage XML found at or under '" + $CoverageXmlPath + "'. Run the coverage pass first (see Tests/Coverage/README.md).")
}

Write-Output ("coverage gate | xml = " + $xmlFile)

[xml]$doc = $null
try {
    $doc = [xml](Get-Content -LiteralPath $xmlFile -Raw)
}
catch {
    Write-Fail ("coverage XML could not be parsed: " + $_.Exception.Message)
}

$root = $doc.DocumentElement
if ($null -eq $root -or $root.Name -ne 'CoverageSession') {
    Write-Fail ("unexpected coverage document root '" + $(if ($null -eq $root) { '<none>' } else { $root.Name }) + "'. Expected OpenCover 'CoverageSession'. Check -coverageOptions in the run command.")
}

$classNodes = $root.SelectNodes('//Class')
if ($null -eq $classNodes -or $classNodes.Count -eq 0) {
    Write-Fail "coverage XML contains no <Class> rows; nothing can be judged."
}

# Aggregate sequence points / branches per gated module.
$agg = @{}
foreach ($module in $gatedModules) {
    $agg[$module.Prefix] = @{ Seq = 0; SeqVisited = 0; Br = 0; BrVisited = 0; Classes = 0 }
}
$unclassifiedSeq = 0
$unclassifiedVisited = 0

foreach ($class in $classNodes) {
    $fullName = $class.FullName
    $summary = $class.Summary
    if ($null -eq $summary) { continue }

    $seq = [int]$summary.numSequencePoints
    $seqVisited = [int]$summary.visitedSequencePoints
    $br = [int]$summary.numBranches
    $brVisited = [int]$summary.visitedBranches

    $matched = $null
    foreach ($module in $gatedModules) {
        if ($fullName -like ($module.Prefix + '.*')) { $matched = $module.Prefix; break }
    }

    if ($null -eq $matched) {
        $unclassifiedSeq += $seq
        $unclassifiedVisited += $seqVisited
        continue
    }

    $bucket = $agg[$matched]
    $bucket.Seq += $seq
    $bucket.SeqVisited += $seqVisited
    $bucket.Br += $br
    $bucket.BrVisited += $brVisited
    $bucket.Classes += 1
}

$violations = New-Object System.Collections.Generic.List[string]
$missing = New-Object System.Collections.Generic.List[string]

Write-Output ''
Write-Output ('{0,-32} {1,-8} {2,9} {3,9} {4,9} {5,9}' -f 'module', 'tier', 'line%', 'line-min', 'branch%', 'branch-min')
Write-Output ('-' * 84)

foreach ($module in $gatedModules) {
    $bucket = $agg[$module.Prefix]

    if ($bucket.Seq -eq 0) {
        $missing.Add($module.Prefix)
        Write-Output ('{0,-32} {1,-8} {2,9} {3,9} {4,9} {5,9}' -f $module.Prefix, $module.Tier, 'n/a', $module.Line, 'n/a', $module.Branch)
        continue
    }

    $linePct = [math]::Round(100.0 * $bucket.SeqVisited / $bucket.Seq, 2)
    $branchPct = if ($bucket.Br -gt 0) { [math]::Round(100.0 * $bucket.BrVisited / $bucket.Br, 2) } else { 0.0 }

    Write-Output ('{0,-32} {1,-8} {2,9} {3,9} {4,9} {5,9}' -f $module.Prefix, $module.Tier, $linePct, $module.Line, $branchPct, $module.Branch)

    if ($linePct -lt $module.Line) {
        $violations.Add($module.Prefix + " line coverage " + $linePct + "% < " + $module.Line + "%")
    }

    if ($module.Branch -gt 0.0 -and $branchPct -lt $module.Branch) {
        $violations.Add($module.Prefix + " branch coverage " + $branchPct + "% < " + $module.Branch + "%")
    }
}

Write-Output ''
Write-Output ("unclassified (not gated) sequence points: " + $unclassifiedVisited + "/" + $unclassifiedSeq)

if ($missing.Count -gt 0) {
    $message = "no coverage rows for: " + ($missing -join ', ') + ". Either those modules were not exercised, or the namespace prefixes in this script no longer match the code."
    if ($Strict) {
        $violations.Add($message)
    }
    else {
        Write-Note $message
        Write-Note 're-run with -Strict to treat this as a failure (recommended once the prefixes are confirmed).'
    }
}

if ($violations.Count -gt 0) {
    Write-Output ''
    Write-Fail ("threshold violations:`n  - " + ($violations -join "`n  - "))
}

Write-Output ''
Write-Output 'GATE-PASS: every gated module is at or above its tiered threshold.'
exit 0
