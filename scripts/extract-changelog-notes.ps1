<#
.SYNOPSIS
    Extracts the CHANGELOG section for one version so it can be published as release notes.

.DESCRIPTION
    Reads a Keep a Changelog file and returns everything between the heading for the requested
    version and the next version heading. Fails when the section is missing or empty, which is the
    signal for the release workflow to stop before it builds anything.
#>
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$resolvedChangelog = (Resolve-Path -LiteralPath $ChangelogPath).Path
$normalizedVersion = $Version.TrimStart('v', 'V').Trim()
if ([string]::IsNullOrWhiteSpace($normalizedVersion)) {
    throw 'A version is required, for example 0.1.0.'
}

$lines = Get-Content -LiteralPath $resolvedChangelog -Encoding UTF8

# A pre-release describes the changes of the version it leads to, so 0.1.0-beta.1 falls back to
# the 0.1.0 section. Rehearsing a release must not require inventing a CHANGELOG entry.
$candidates = @($normalizedVersion)
$baseVersion = ($normalizedVersion -split '-', 2)[0]
if ($baseVersion -ne $normalizedVersion) { $candidates += $baseVersion }

$startIndex = -1
$matchedVersion = $null
foreach ($candidate in $candidates) {
    # Accepts "## 0.1.0" and "## [0.1.0] - 2026-08-06" without matching "## 0.1.0-beta.1".
    $headingPattern = '^##\s+\[?' + [regex]::Escape($candidate) + '\]?(\s|$|[:(])'
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match $headingPattern) {
            $startIndex = $index + 1
            $matchedVersion = $candidate
            break
        }
    }
    if ($startIndex -ge 0) { break }
}

if ($startIndex -lt 0) {
    throw "CHANGELOG.md has no section for $($candidates -join ' or '). Add one before tagging."
}

$endIndex = $lines.Count
for ($index = $startIndex; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^##\s') {
        $endIndex = $index
        break
    }
}

$section = if ($endIndex -gt $startIndex) { $lines[$startIndex..($endIndex - 1)] } else { @() }
$notes = ($section -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "The CHANGELOG section for version $matchedVersion is empty."
}

if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Set-Content -LiteralPath $OutputPath -Value $notes -Encoding UTF8
}

$notes
