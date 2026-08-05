param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [Parameter(Mandatory)]
    [ValidateSet('Windows10', 'Windows11')]
    [string]$ExpectedOs,

    [Parameter(Mandatory)]
    [ValidateSet('X64', 'Arm64')]
    [string]$ExpectedArchitecture,

    [Parameter(Mandatory)]
    [ValidateSet(100, 150, 200)]
    [int]$ExpectedScale,

    [ValidateSet('Any', 'Enabled', 'Disabled')]
    [string]$ExpectedHighContrast = 'Any',

    [ValidateSet('Any', 'Enabled', 'Disabled')]
    [string]$ExpectedAnimations = 'Any',

    [ValidateRange(2, 30)]
    [int]$StartupTimeoutSeconds = 15,

    [string]$SeedDatabasePath,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\quality-matrix')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

public static class QualityMatrixNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
}
'@
Add-Type -TypeDefinition $nativeSource

function Get-ExecutableArchitecture([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "The executable is not a valid PE file: $Path"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length) {
        throw "The executable has an invalid PE header: $Path"
    }

    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    switch ($machine) {
        0x8664 { return 'X64' }
        0xAA64 { return 'Arm64' }
        default { return ('Unknown-0x{0:X4}' -f $machine) }
    }
}

function Get-OperatingSystemArchitecture {
    $nativeArchitecture = if ($env:PROCESSOR_ARCHITEW6432) {
        $env:PROCESSOR_ARCHITEW6432
    }
    else {
        $env:PROCESSOR_ARCHITECTURE
    }

    if ($nativeArchitecture -eq 'ARM64') { return 'Arm64' }
    if ($nativeArchitecture -eq 'AMD64') { return 'X64' }

    $runtimeArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($runtimeArchitecture -eq 'Arm64') { return 'Arm64' }
    if ($runtimeArchitecture -eq 'X64') { return 'X64' }
    return $runtimeArchitecture
}

function Get-PrimaryDpi {
    [uint32]$dpiX = 0
    [uint32]$dpiY = 0
    $monitor = [QualityMatrixNative]::MonitorFromWindow([QualityMatrixNative]::GetDesktopWindow(), 2)
    $result = [QualityMatrixNative]::GetDpiForMonitor($monitor, 0, [ref]$dpiX, [ref]$dpiY)
    if ($result -ne 0 -or $dpiX -eq 0) {
        $dpiX = [QualityMatrixNative]::GetDpiForSystem()
        $dpiY = $dpiX
    }

    [pscustomobject]@{
        X = [int]$dpiX
        Y = [int]$dpiY
        Scale = [int][Math]::Round(($dpiX / 96.0) * 100)
    }
}

function Assert-Expected([string]$Name, $Actual, $Expected) {
    if ($Actual -ne $Expected) {
        throw "$Name mismatch. Expected '$Expected', detected '$Actual'. The quality result was not recorded under a false environment label."
    }
}

function Save-WindowImage([IntPtr]$Handle, [string]$Path) {
    $rect = New-Object QualityMatrixNative+Rect
    if (-not [QualityMatrixNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw 'Could not read the dashboard window bounds.'
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "The dashboard has invalid bounds: ${width}x${height}."
    }

    $bitmap = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        try {
            if (-not [QualityMatrixNative]::PrintWindow($Handle, $deviceContext, 2)) {
                throw 'PrintWindow could not capture the dashboard.'
            }
        }
        finally {
            $graphics.ReleaseHdc($deviceContext)
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    [pscustomobject]@{ Width = $width; Height = $height }
}

function Save-VisibleWindowImage([IntPtr]$Handle, [string]$Path) {
    $rect = New-Object QualityMatrixNative+Rect
    if (-not [QualityMatrixNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw 'Could not read the dashboard window bounds.'
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $null = [QualityMatrixNative]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 200
    $bitmap = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object Drawing.Size $width, $height))
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Find-AutomationElementByName($Root, [string]$Name) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-AutomationElementByNameWithPattern($Root, [string]$Name, $Pattern) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $matches = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    for ($index = 0; $index -lt $matches.Count; $index++) {
        $candidate = $matches.Item($index)
        for ($depth = 0; $null -ne $candidate -and $depth -lt 6; $depth++) {
            $candidatePattern = $null
            if ($candidate.TryGetCurrentPattern($Pattern, [ref]$candidatePattern)) {
                return $candidate
            }
            $candidate = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($candidate)
        }
    }
    return $null
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedSeedDatabase = if ([string]::IsNullOrWhiteSpace($SeedDatabasePath)) {
    $null
}
else {
    (Resolve-Path -LiteralPath $SeedDatabasePath).Path
}
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$os = Get-CimInstance Win32_OperatingSystem
$build = [int]$os.BuildNumber
$actualOs = if ($build -ge 22000) { 'Windows11' } else { 'Windows10' }
$actualArchitecture = Get-OperatingSystemArchitecture
$executableArchitecture = Get-ExecutableArchitecture $resolvedExecutable
$dpi = Get-PrimaryDpi
$highContrast = [System.Windows.Forms.SystemInformation]::HighContrast
$animations = [System.Windows.SystemParameters]::ClientAreaAnimation

Assert-Expected 'Operating system' $actualOs $ExpectedOs
if ($ExpectedOs -eq 'Windows10' -and $build -lt 19045) {
    throw "Windows 10 build 19045 (22H2) or newer is required; detected build $build."
}
Assert-Expected 'Operating-system architecture' $actualArchitecture $ExpectedArchitecture
Assert-Expected 'Executable architecture' $executableArchitecture $ExpectedArchitecture
Assert-Expected 'Primary display scale' $dpi.Scale $ExpectedScale
if ($ExpectedHighContrast -ne 'Any') {
    Assert-Expected 'High contrast' $highContrast ($ExpectedHighContrast -eq 'Enabled')
}
if ($ExpectedAnimations -ne 'Any') {
    Assert-Expected 'Animations' $animations ($ExpectedAnimations -eq 'Enabled')
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runs = [Collections.Generic.List[object]]::new()
$languages = @(
    [pscustomobject]@{ Code = 'es'; Summary = 'Resumen'; History = 'Historial'; Filter = 'Filtrar por proveedor'; EmptyPrefix = 'No hay observaciones para este filtro.'; CountPattern = '^\d[\d,.]* observaciones' },
    [pscustomobject]@{ Code = 'en'; Summary = 'Summary'; History = 'History'; Filter = 'Filter by provider'; EmptyPrefix = 'There are no observations for this filter.'; CountPattern = '^\d[\d,.]* observations' }
)
$themes = @('System', 'Dark', 'Light')

foreach ($language in $languages) {
    foreach ($theme in $themes) {
        $dataDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ("AIVitals.Quality." + [Guid]::NewGuid().ToString('N'))))
        New-Item -ItemType Directory -Path $dataDirectory | Out-Null
        if ($null -ne $resolvedSeedDatabase) {
            Copy-Item -LiteralPath $resolvedSeedDatabase -Destination (Join-Path $dataDirectory 'usage.db')
        }
        $preferences = [ordered]@{
            schemaVersion = 1
            startMinimized = $false
            theme = $theme
            language = $language.Code
            fakeAdapterEnabled = $true
            widget = [ordered]@{
                isVisible = $false
                mode = 0
                isLocked = $false
                isClickThrough = $false
                pinnedProviderIds = @('codex', 'claude-code')
            }
            onboardingCompleted = $true
        }
        $preferences | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dataDirectory 'preferences.json') -Encoding UTF8

        $previousDataDirectory = $env:AI_VITALS_DATA_DIRECTORY
        $previousSkipClaudeInstaller = $env:AI_VITALS_SKIP_CLAUDE_INSTALLER
        $env:AI_VITALS_DATA_DIRECTORY = $dataDirectory
        $env:AI_VITALS_SKIP_CLAUDE_INSTALLER = '1'
        try {
            $process = Start-Process -FilePath $resolvedExecutable -PassThru
        }
        finally {
            $env:AI_VITALS_DATA_DIRECTORY = $previousDataDirectory
            $env:AI_VITALS_SKIP_CLAUDE_INSTALLER = $previousSkipClaudeInstaller
        }

        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
            $handle = [IntPtr]::Zero
            while ([DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 200
                $process.Refresh()
                if ($process.HasExited) {
                    throw "The app exited during the $($language.Code)/$theme startup with code $($process.ExitCode)."
                }
                if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                    $handle = $process.MainWindowHandle
                    break
                }
            }
            if ($handle -eq [IntPtr]::Zero) {
                throw "The dashboard did not expose a window within $StartupTimeoutSeconds seconds for $($language.Code)/$theme."
            }

            $root = [Windows.Automation.AutomationElement]::FromHandle($handle)
            if ($null -eq $root) { throw 'UI Automation could not inspect the dashboard.' }
            $automationElements = $root.FindAll(
                [Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.Condition]::TrueCondition)
            $visibleNames = for ($elementIndex = 0; $elementIndex -lt $automationElements.Count; $elementIndex++) {
                $name = $automationElements.Item($elementIndex).Current.Name
                if (-not [string]::IsNullOrWhiteSpace($name)) { $name }
            }
            if ($null -eq (Find-AutomationElementByName $root $language.Summary)) {
                $sample = ($visibleNames | Select-Object -Unique -First 40) -join ' | '
                throw "The localized Summary tab '$($language.Summary)' was not exposed to UI Automation. Root='$($root.Current.Name)'. Exposed names: $sample"
            }

            $suffix = '{0}-{1}-{2}pct-{3}-{4}-hc-{5}-motion-{6}' -f `
                $actualOs.ToLowerInvariant(), $actualArchitecture.ToLowerInvariant(), $dpi.Scale,
                $language.Code, $theme.ToLowerInvariant(), $highContrast.ToString().ToLowerInvariant(),
                $animations.ToString().ToLowerInvariant()
            $summaryScreenshotPath = Join-Path $resolvedOutputDirectory ($suffix + '-summary.png')
            $size = Save-WindowImage $handle $summaryScreenshotPath

            $historyTab = Find-AutomationElementByNameWithPattern $root $language.History ([Windows.Automation.SelectionItemPattern]::Pattern)
            if ($null -eq $historyTab) {
                throw "The localized History tab '$($language.History)' was not exposed to UI Automation."
            }
            $selection = $historyTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
            $selection.Select()
            Start-Sleep -Milliseconds 300
            if ($null -eq (Find-AutomationElementByName $root $language.Filter)) {
                throw "The localized provider filter accessible name '$($language.Filter)' was not exposed."
            }
            $providerFilter = Find-AutomationElementByNameWithPattern $root $language.Filter ([Windows.Automation.ExpandCollapsePattern]::Pattern)
            if ($null -eq $providerFilter) {
                throw "The localized provider filter '$($language.Filter)' did not expose the expand/collapse pattern."
            }
            $expandCollapse = $providerFilter.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            $expandCollapse.Expand()
            Start-Sleep -Milliseconds 250
            $dropdownScreenshotPath = Join-Path $resolvedOutputDirectory ($suffix + '-dropdown.png')
            Save-VisibleWindowImage $handle $dropdownScreenshotPath
            $expandCollapse.Collapse()
            $historyElements = $root.FindAll(
                [Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.Condition]::TrueCondition)
            $historyNames = for ($elementIndex = 0; $elementIndex -lt $historyElements.Count; $elementIndex++) {
                $name = $historyElements.Item($elementIndex).Current.Name
                if (-not [string]::IsNullOrWhiteSpace($name)) { $name }
            }
            $localizedHistoryStatus = $historyNames | Where-Object {
                $_.StartsWith($language.EmptyPrefix, [StringComparison]::Ordinal) -or $_ -match $language.CountPattern
            } | Select-Object -First 1
            $hasLocalizedHistoryStatus = $null -ne $localizedHistoryStatus
            if (-not $hasLocalizedHistoryStatus) {
                $sample = ($historyNames | Select-Object -Unique -First 60) -join ' | '
                throw "No localized history status was exposed for '$($language.Code)'. Exposed names: $sample"
            }

            $historyScreenshotPath = Join-Path $resolvedOutputDirectory ($suffix + '-history.png')
            $null = Save-WindowImage $handle $historyScreenshotPath
            $elementCount = $automationElements.Count

            $runs.Add([pscustomobject]@{
                Language = $language.Code
                Theme = $theme
                WindowTitle = $root.Current.Name
                Width = $size.Width
                Height = $size.Height
                AutomationElementCount = $elementCount
                LocalizedSummary = $language.Summary
                LocalizedProviderFilter = $language.Filter
                LocalizedHistoryStatus = $true
                SummaryScreenshot = $summaryScreenshotPath
                HistoryScreenshot = $historyScreenshotPath
                DropdownScreenshot = $dropdownScreenshotPath
                Passed = $true
            })
        }
        finally {
            $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($null -ne $running) {
                Stop-Process -Id $process.Id
                $process.WaitForExit(5000) | Out-Null
            }
            if ($dataDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $dataDirectory)) {
                Remove-Item -LiteralPath $dataDirectory -Recurse -Force
            }
        }
    }
}

$environmentKey = '{0}-{1}-{2}pct-hc-{3}-motion-{4}' -f `
    $actualOs.ToLowerInvariant(), $actualArchitecture.ToLowerInvariant(), $dpi.Scale,
    $highContrast.ToString().ToLowerInvariant(), $animations.ToString().ToLowerInvariant()
$reportPath = Join-Path $resolvedOutputDirectory ($environmentKey + '.json')
$report = [ordered]@{
    SchemaVersion = 1
    RecordedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Passed = $true
    OperatingSystem = [ordered]@{
        Expected = $ExpectedOs
        Detected = $actualOs
        Caption = $os.Caption
        Version = $os.Version
        Build = $build
    }
    Architecture = [ordered]@{
        Expected = $ExpectedArchitecture
        OperatingSystem = $actualArchitecture
        Executable = $executableArchitecture
    }
    Display = [ordered]@{
        ExpectedScale = $ExpectedScale
        DpiX = $dpi.X
        DpiY = $dpi.Y
        Scale = $dpi.Scale
        MonitorCount = [System.Windows.Forms.Screen]::AllScreens.Count
    }
    Accessibility = [ordered]@{
        HighContrast = $highContrast
        AnimationsEnabled = $animations
        ExpectedHighContrast = $ExpectedHighContrast
        ExpectedAnimations = $ExpectedAnimations
    }
    Executable = $resolvedExecutable
    SeedDatabase = $resolvedSeedDatabase
    Runs = $runs
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "Quality matrix cell passed: $environmentKey"
Write-Output "Evidence: $reportPath"
