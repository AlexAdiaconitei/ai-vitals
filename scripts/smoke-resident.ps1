param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [ValidateRange(1, 30)]
    [int]$WaitSeconds = 2
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeDataDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ("AIVitals.Smoke." + [Guid]::NewGuid().ToString("N"))))
New-Item -ItemType Directory -Path $smokeDataDirectory | Out-Null
$previousDataDirectory = $env:AI_VITALS_DATA_DIRECTORY
$previousSkipClaudeInstaller = $env:AI_VITALS_SKIP_CLAUDE_INSTALLER
$env:AI_VITALS_DATA_DIRECTORY = $smokeDataDirectory
$env:AI_VITALS_SKIP_CLAUDE_INSTALLER = '1'
try {
    $process = Start-Process -FilePath $resolvedExecutable -PassThru -WindowStyle Hidden
}
finally {
    $env:AI_VITALS_DATA_DIRECTORY = $previousDataDirectory
    $env:AI_VITALS_SKIP_CLAUDE_INSTALLER = $previousSkipClaudeInstaller
}

try {
    Start-Sleep -Seconds $WaitSeconds
    $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -eq $running) {
        throw "Resident process exited during startup with code $($process.ExitCode)."
    }
}
finally {
    $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -ne $running) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(5000) | Out-Null
    }
    if ($smokeDataDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $smokeDataDirectory)) {
        Remove-Item -LiteralPath $smokeDataDirectory -Recurse -Force
    }
}
