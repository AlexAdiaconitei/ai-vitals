param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $workspaceRoot "src\AIVitals.App\AIVitals.App.csproj"
$appOutput = Join-Path $workspaceRoot "src\AIVitals.App\bin\$Configuration\net9.0-windows"
$bridgePath = Join-Path $appOutput "statusline\AIVitals.ClaudeCode.StatusLine.exe"

dotnet build $appProject --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar AIVitals.App."
}

if (-not (Test-Path -LiteralPath $bridgePath -PathType Leaf)) {
    throw "No se encontró el bridge de Claude Code en la salida de la aplicación: $bridgePath"
}

Write-Output "Bridge de Claude Code verificado: $bridgePath"
