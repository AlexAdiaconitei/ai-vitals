<#
.SYNOPSIS
    Publishes AI Vitals for one runtime and packs it into a Velopack installer.

.DESCRIPTION
    Produces the installer and packages for a single architecture. Each architecture uses its own
    Velopack channel so an installed copy only ever updates to packages built for its own runtime.
    Signing is optional: pass -SignParameters to hand signtool arguments to Velopack.
#>
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [string]$ReleaseNotesPath,

    [string]$OutputDirectory,

    [string]$PublishDirectory,

    [string]$SignParameters
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$normalizedVersion = $Version.TrimStart('v', 'V').Trim()
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a semantic version such as 0.1.0 or 0.1.0-beta.1."
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts\releases' }
if (-not $PublishDirectory) { $PublishDirectory = Join-Path $repositoryRoot "artifacts\publish\$Runtime" }
$projectPath = Join-Path $repositoryRoot 'src\AIVitals.App\AIVitals.App.csproj'
$iconPath = Join-Path $repositoryRoot 'src\AIVitals.App\Assets\AppIcon.ico'

if (Test-Path -LiteralPath $PublishDirectory) {
    Remove-Item -LiteralPath $PublishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Publishing AI Vitals $normalizedVersion for $Runtime"
dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDirectory `
    "-p:Version=$normalizedVersion"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime." }

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "The Velopack CLI is missing. Install it with: dotnet tool install -g vpk"
}

# The pack id also names the install directory under %LocalAppData%, so it stays distinct from
# the AIVitals data directory that must survive uninstalling.
$packArguments = @(
    'pack'
    '--packId', 'AIVitalsApp'
    '--packVersion', $normalizedVersion
    '--packDir', $PublishDirectory
    '--packTitle', 'AI Vitals'
    '--packAuthors', 'Alex Adiaconitei'
    '--mainExe', 'AIVitals.App.exe'
    '--icon', $iconPath
    '--channel', $Runtime
    '--runtime', $Runtime
    '--outputDir', $OutputDirectory
    # The installer and the update packages are the deliverables; a portable bundle would add
    # another ~110 MB per architecture to every release for a path nothing else supports.
    '--noPortable', 'true'
)
if ($ReleaseNotesPath) { $packArguments += @('--releaseNotes', (Resolve-Path -LiteralPath $ReleaseNotesPath).Path) }
if ($SignParameters) { $packArguments += @('--signParams', $SignParameters) }

Write-Host "Packing channel $Runtime into $OutputDirectory"
& vpk @packArguments
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed for $Runtime." }
