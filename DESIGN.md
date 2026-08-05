# AI Vitals Design System

The canonical reference for keeping the dashboard, widgets, quick popup, and tray menu consistent. This document describes the current implementation and the rules future changes should follow.

## 1. Visual direction

AI Vitals is a local observation tool for people who use multiple AI coding assistants. Its primary job is to make quotas and provider health readable at a glance without looking like a generic SaaS dashboard or an overdecorated “AI” interface.

The direction is summarized as:

- **local instrument:** dark, precise, compact, and quiet;
- **functional glass:** transparency in widgets and popovers, never in bars or text;
- **data first:** color distinguishes providers, state, or selection; it does not decorate;
- **one visual signature:** the application's three activity rings;
- **restrained controls:** neutral borders by default and color only for selection, focus, or state.

Do not rely on flashy gradients, glows, multicolor borders, oversized promotional headlines, or cards that look copied from a generic web template.

## 2. Signature: activity rings

The application icon contains three open concentric rings designed for immediate activity reading:

| Ring | Dark-theme color | Visual meaning |
| --- | --- | --- |
| Outer | `#25DCCD` / `SignalBrush` | Application identity and positive state |
| Middle | `#3D8BFF` / `CodexBrush` | Codex |
| Inner | `#FF8A4C` or `#FF9B2F` / `WarmBrush` | Claude and warm signal |

Rules:

- start arcs at 12 o'clock and use rounded ends;
- keep a visible dark track behind progress;
- do not add letters inside the application icon;
- do not enclose the rings in a colored square;
- for nested quotas, place the longest-duration window outside and the shortest inside;
- when the center already contains the app or provider icon, do not add decorative blue/orange corner bars.

Assets:

- `src/AIVitals.App/Assets/AppIcon.svg`: master vector;
- `src/AIVitals.App/Assets/AppIcon.png`: 256 px preview;
- `src/AIVitals.App/Assets/AppIcon.ico`: Windows, taskbar, and executable icon;
- `scripts/generate-app-icon.ps1`: generates the PNG and multi-resolution ICO (`16, 20, 24, 32, 40, 48, 64, 128, 256`).

## 3. Color

### Essential dark palette

| Name | Hex | Use |
| --- | --- | --- |
| Midnight | `#07111F` | Main canvas |
| Deep panel | `#0D1B2B` | Cards and panels |
| Raised surface | `#122238` | Fields, rows, and controls |
| Signal teal | `#31D6C6` | Application, success, and primary action |
| Codex blue | `#3D8BFF` | Codex data and states |
| Claude orange | `#FF9B2F` | Claude data and states |

### WPF tokens

Do not use hexadecimal colors directly in XAML except in resource definitions or assets. Components consume `DynamicResource` so theme, language, and high contrast can change at runtime.

| Resource | Dark | Light | Use |
| --- | --- | --- | --- |
| `CanvasBrush` | `#07111F` | `#EEF3F8` | Dashboard background |
| `SidebarBrush` | `#091524` | `#E6EDF5` | Side navigation |
| `TopBarBrush` | `#07101B` | `#F8FAFD` | Header and footer |
| `PanelBrush` | `#0D1B2B` | `#FFFFFF` | Main cards |
| `PanelAltBrush` | `#0A1726` | `#F7FAFD` | Summary cards |
| `SurfaceBrush` | `#122238` | `#E8EFF6` | Rows and controls |
| `ElevatedBrush` | `#172A42` | `#DCE7F1` | Hover and elevation |
| `SelectionBrush` | `#163B55` | `#CCEAE7` | Active selection |
| `WidgetGlassBrush` | `#B80A1726` | `#D9F7FAFD` | Outer glass |
| `WidgetSurfaceBrush` | `#681A2B3D` | `#A6DCE7F1` | Controls on glass |
| `LineBrush` | `#263B53` | `#B9C7D6` | Borders, tracks, dividers |
| `TextBrush` | `#F5F8FC` | `#132033` | Primary text |
| `MutedTextBrush` | `#B7C4D3` | `#526277` | Secondary text |
| `SignalBrush` | `#31D6C6` | `#087F78` | App identity, success, CTA |
| `CodexBrush` | `#3D8BFF` | `#1769D2` | Codex |
| `WarmBrush` | `#FF9B2F` | `#965300` | Claude and warm selection |
| `ClaudeBrandBrush` | `#D97757` | `#A94F36` | Official Claude SVG |
| `DangerBrush` | `#FF9B9B` | `#A33141` | Destructive action |

`WindowsAppearance.Apply` is the source of truth for themes. In high contrast, every token must map to system colors; never preserve a brand color when it reduces legibility.

### Color usage

- A bar always retains its provider color.
- Teal means application identity, automatic connection, visibility, or positive selection.
- Blue may indicate Codex or the selected widget mode.
- Orange may indicate Claude or a selected theme; context must prevent ambiguity.
- Normal borders use `LineBrush`. Do not color every border in a section.
- A value, bar, or status may use color; explanatory text remains on `TextBrush` or `MutedTextBrush`.

## 4. Typography

| Role | Family | Approximate size | Weight |
| --- | --- | --- | --- |
| UI and body | `Segoe UI Variable Text, Segoe UI` | 11–13 px | Regular |
| Page title | Same family | 28 px | SemiBold |
| Card title | Same family | 15–18 px | SemiBold |
| Primary value | `Cascadia Mono, Consolas` | 17–25 px | Bold |
| Technical label | `Cascadia Mono, Consolas` | 9–10 px | Regular/SemiBold |
| Compact widget | Both families by role | 8–10 px | SemiBold/Bold |

Rules:

- Segoe communicates actions and meaning; Cascadia aligns numbers, shortcuts, and short labels.
- Do not uppercase complete sentences. Reserve uppercase for status labels and short eyebrows.
- Avoid body text below 10 px. Use 8–9 px only for `5H`, `W`, and compact-widget numbers with high contrast.
- Values must not compete with the provider name. Use only one dominant figure per block.
- Explicitly center text vertically in badges, pills, and buttons.

## 5. Iconography

### Interface icons

Use `Segoe Fluent Icons` with `Segoe MDL2 Assets` fallback through the `FluentIcon` style. Do not add another library for actions already available in Fluent.

| Action | Glyph |
| --- | --- |
| Dashboard | `E80F` |
| Show widget | `E890` |
| Widget/layout | `E7F4` / `ECA5` |
| Lock | `E72E` |
| Unlock | `E785` |
| Click-through/pin | `E718` |
| Recover/refresh | `E72C` |
| Move to display | `E7C2` |
| Settings | `E713` |
| Exit | `E7E8` |

Icon-only buttons always need a `ToolTip` and `AutomationProperties.Name`. Normal icon size is 13–18 px inside a 34–46 px interactive area.

### Provider SVGs

Provider logos were downloaded from [SVGL](https://svgl.app/) and are bundled locally so runtime behavior does not depend on the network:

| Provider | File | Color |
| --- | --- | --- |
| Claude | `Assets/Providers/claude-ai.svg` | Original `#D97757`, tintable with `ClaudeBrandBrush` |
| Codex dark | `Assets/Providers/codex-dark.svg` | Original white, tintable with `CodexBrush` |
| Codex light | `Assets/Providers/codex-light.svg` | Variant for light surfaces |

Render provider assets with `SvgLogo`. It extracts the geometry and supports `Tint`; do not use emoji, scaled PNGs, or approximate brand recreations.

To add a provider:

1. Find the official SVG on SVGL or from the provider's official source.
2. Save it under `src/AIVitals.App/Assets/Providers/`.
3. Preserve `viewBox`, `fill-rule`, and geometry; remove scripts and active metadata.
4. Add a light/dark variant only when tinting does not preserve the brand.
5. Verify at 14, 18, 24, and 40 px.
6. Add a semantic color resource instead of repeating hex values in views.
7. Record the source and license here when they differ from SVGL.

## 6. Surfaces, borders, and elevation

- Dashboard surfaces are opaque and remain stable while scrolling.
- Widgets and tray surfaces use `AllowsTransparency=True`, a transparent outer background, and `WidgetGlassBrush` panels.
- Bars and text remain opaque on glass.
- Card radius: 7–10 px.
- Widget/popover radius: 15–16 px.
- Button radius: 6–8 px.
- Normal border: 1 px with `LineBrush`.
- Widget shadow: blur 17, depth 4, approximate opacity 0.32.
- Tray/popup shadow: blur 22–26, depth 6–7, opacity 0.42–0.48.

Do not stack a shadow, colored border, and selected background unless all three are required to communicate state. Before adding decoration, remove one layer and verify that the component remains understandable.

## 7. Spacing and composition

Preferred scale: `4, 6, 8, 10, 12, 14, 16, 18, 22` px.

- Related-card gap: 10–14 px.
- Card padding: 14–18 px.
- Widget padding: 8–11 px.
- Icon/text gap: 5–10 px.
- Dashboard header: 54 px.
- Sidebar: 202 px.
- Dashboard footer: 28 px.
- Main window: 1440 × 860; minimum 1080 × 700.

The sidebar uses a neutral border and one selected surface. Do not add different colored outline pills for each section.

## 8. Widgets

### Shared rules

- Use a glass background, neutral border, and short shadow.
- Keep the header compact with the app icon and an interaction-state glyph.
- Free, locked, and click-through states never consume space as visible text; expose them as accessible glyphs with tooltips.
- A click opens the quick panel when locked; a double-click opens the dashboard.
- Use short quota labels: `5H`, `W`, `W·S`, `W·O`, or compact `D`/`M`/duration labels for future windows.
- Do not show long labels such as “immediate” or “total” inside the widget.

### Rings

- One connection: `146 × 178`.
- Two connections: `274 × 178`.
- More than two: `274 × 294`.
- Current maximum diameter: 74 px. Each inner window shrinks by 15 px, with a 29 px minimum.
- Place the longest window outside and the shortest inside.
- Center the provider logo inside the rings.

### Horizontal bars

- Fixed width: 420 px.
- Dynamic height: `58 + bands × 17 + connections × 4`.
- Each band occupies 17 px with a 7 px track.
- Structure: provider icon/name, short label, bar, and percentage.
- Keep bars close together; do not create a full card for every limit.

### Vertical bars

- Fixed height: 420 px, matching the horizontal widget width.
- Dynamic compact width; the current Codex + Claude reference is 144 px.
- Each bar is 9 px wide inside a 27 px column and a 340 px plot area.
- Show only the provider icon, never icon plus name.
- Hide the application name in the vertical header to prevent truncation; retain the app icon and state glyph.

## 9. Tray menu and quick popup

Right-click does not use the native `ContextMenuStrip`. It opens a `344 × 390` WPF popover that shares the widget background, shadow, typography, and radii.

- Header: activity rings, AI Vitals name, `LOCAL · TRAY` eyebrow, and dashboard access.
- Keep all widget actions in one block.
- Quick actions: show, lock, click-through, recover, and move.
- Modes and themes use segmented controls; only the selected choice receives color.
- Separate Settings and Exit in the footer.
- Clicking outside or pressing `Esc` closes the popover.

Left-click opens the quick-status popup. The popup can control the widget without forcing the dashboard to open.

## 10. Dashboard

- Use restrained side navigation and make content the dominant surface.
- The summary combines quotas, chart, widget preview, observations, and adapter health.
- The Widget section previews the selected mode with current data.
- The history table shows `Time`, `Provider`, `Capability`, `Value`, and `Context`. Technical quality remains in storage/export and is not a visual column.
- Format units for people: `45.7 s`, `3 min 05 s`, `40%`, `1,180 tokens`, `1.6628 USD`; never expose `45678 milliseconds`.
- Context may show the model, `Account`, or a short pseudonymous session identifier. Never show prompts, paths, or private session titles.
- The header places **Recover widget** next to the shortcut and explains it in a tooltip.
- The sidebar footer links GitHub and Ko-fi; it is not a theme-status panel.

## 11. Language and states

Write from the user's task, not from the implementation.

Preferred copy:

- “OAuth quotas connected automatically.”
- “Recover widget.”
- “Context usage,” “Duration,” and “Account.”
- “Waiting for data” before a reading exists.
- “Last known value” when an observation is stale.

Avoid:

- claiming OAuth quotas depend on Claude's first response;
- manual connect/disconnect controls when detection is automatic;
- “visual mode” without showing all choices together;
- “immediate” and “total” as in-widget window names;
- converting absence, timeout, or error into `0%`;
- internal terms such as bridge, mapper, or statusLine in primary actions. Mention the status line only as a secondary explanation of optional telemetry.

## 12. Accessibility

- Windows high contrast takes priority over theme and brand.
- Every interactive icon needs a tooltip and automation name.
- Keep focus visible; focus may use `WarmBrush` with a 2 px border.
- Never rely on color alone. Pair state with a glyph, text, or spatial selection.
- Restrict 8–9 px figures to the widget; the dashboard uses larger text.
- Scrollbars use a transparent track and rounded 9 px thumb with a 30 px minimum.
- Respect `SystemParameters.ClientAreaAnimation`; do not add motion when Windows disables it.

## 13. Components and sources of truth

| Area | Primary file |
| --- | --- |
| Base tokens | `src/AIVitals.App/App.xaml` |
| Runtime theme and language | `src/AIVitals.App/WindowsAppearance.cs` |
| Dashboard | `src/AIVitals.App/MainWindow.xaml` |
| Widget | `src/AIVitals.App/WidgetWindow.xaml` |
| Widget geometry | `src/AIVitals.App/WidgetWindow.xaml.cs` |
| Widget projection | `src/AIVitals.App/WidgetViewModel.cs` |
| Tray menu | `src/AIVitals.App/TrayMenuWindow.xaml` |
| Quick popup | `src/AIVitals.App/QuickPopupWindow.xaml` |
| Bar/ring controls | `UsageMeter`, `UsageRing`, `ActivityRingsIcon` |
| SVG logos | `src/AIVitals.App/SvgLogo.cs` and `Assets/Providers/` |
| ES/EN copy | `src/AIVitals.Application/UiLanguageCatalog.cs` |

## 14. Checklist for future changes

Before accepting a visual change:

- [ ] It uses dynamic resources, not repeated hex values.
- [ ] It works in dark, light, and high-contrast modes.
- [ ] Secondary text and small values remain legible.
- [ ] Every colored border communicates meaning.
- [ ] Actions use Fluent icons and providers use official SVGs.
- [ ] Icon-only controls have tooltips and accessible names.
- [ ] Every published quota window is shown once.
- [ ] Labels are short and units are human-readable.
- [ ] No prompts, paths, private titles, or credentials are exposed.
- [ ] Geometry changes are verified at 100%, 150%, and 200% scaling.
- [ ] Dashboard, popup, tray, and all three widgets are visually captured.
- [ ] This document is updated when a design-system rule changes.

The baseline automated check is `scripts/verify-windows-quality-matrix.ps1`. Supplement it with visual inspection of the tray and all three widget geometries because they are independent windows outside the dashboard.
