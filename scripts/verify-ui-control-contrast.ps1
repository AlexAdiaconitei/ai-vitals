$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$xamlPath = Join-Path $PSScriptRoot '..\src\AIVitals.App\MainWindow.xaml'
$xaml = [IO.File]::ReadAllText([IO.Path]::GetFullPath($xamlPath))

$requirements = [ordered]@{
    'custom ComboBox template' = '<ControlTemplate TargetType="ComboBox">'
    'explicit ComboBoxItem style' = '<Style TargetType="ComboBoxItem">'
    'explicit DataGridRow style' = '<Style TargetType="DataGridRow">'
    'selected row contrast trigger' = '<Trigger Property="IsSelected" Value="True">'
}

foreach ($requirement in $requirements.GetEnumerator()) {
    if ($xaml.IndexOf($requirement.Value, [StringComparison]::Ordinal) -lt 0) {
        throw "Missing $($requirement.Key). Native WPF colors can override the app palette and make dropdown/table text unreadable."
    }
}

$forbiddenPairs = @(
    'Background="White"',
    'Foreground="White"',
    'Background="#FFFFFF"',
    'Foreground="#FFFFFF"'
)
foreach ($pair in $forbiddenPairs) {
    if ($xaml.IndexOf($pair, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Hard-coded control color '$pair' bypasses the theme palette."
    }
}

Write-Output 'Dropdown and table contrast contract passed.'
