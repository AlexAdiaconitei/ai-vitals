# Windows Quality Matrix

Status on 2026-08-05: **1 of 12 cells executed in a real environment**. The `win-x64` and `win-arm64` publications build successfully, but publishing does not count as executing that architecture.

| OS | Architecture | 100% | 150% | 200% |
| --- | ---: | ---: | ---: | ---: |
| Windows 11 | x64 | ✅ Build 26200 | Pending | Pending |
| Windows 11 | ARM64 | Pending | Pending | Pending |
| Windows 10 22H2 | x64 | Pending | Pending | Pending |
| Windows 10 22H2 | ARM64 | Pending | Pending | Pending |

The completed cell covered all six language/theme combinations (`es`/`en` × `System`/`Dark`/`Light`) at 96 DPI. Every run verified:

- the resident process and visible dashboard;
- executable PE architecture and native OS architecture;
- OS, build, effective DPI, and monitor count;
- History navigation through UI Automation;
- localized accessible names for the summary, history, and provider filter;
- localized history state;
- PNG screenshots and JSON evidence.

The review found and fixed residual Spanish strings in accessible names, dialogs, widget state, and history. Smoke tests set `AI_VITALS_SKIP_CLAUDE_INSTALLER=1`, so they never modify the real Claude Code configuration.

## Run one matrix cell

First publish for the machine's native architecture:

```powershell
dotnet publish src\AIVitals.App\AIVitals.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts\win-x64
```

Then run the gate with the environment's real values:

```powershell
.\scripts\verify-windows-quality-matrix.ps1 `
  -ExecutablePath .\artifacts\win-x64\AIVitals.App.exe `
  -ExpectedOs Windows11 `
  -ExpectedArchitecture X64 `
  -ExpectedScale 150
```

For ARM64, replace `win-x64`/`X64` with `win-arm64`/`Arm64`. The script fails before recording evidence when it detects an incorrect label. Windows 10 is accepted only from build 19045 (22H2).

At least one additional run must use high contrast, and another must use reduced motion:

```powershell
-ExpectedHighContrast Enabled
-ExpectedAnimations Disabled
```

Phase 06 must not begin until all twelve cells are complete and the generated screenshots have been reviewed.
