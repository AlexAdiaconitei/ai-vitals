# Changelog

All notable changes to AI Vitals are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- A waiting update is announced where the application is actually visible: the tray icon carries a dot, a system notification is raised once per version, the left-click quick view carries the same install entry as the right-click menu, and the dashboard's About tab shows a dot in its navigation entry.
- A native C# activity traffic-light widget can be enabled independently for Claude Code and Codex. It tracks concurrent tools and sessions, starts and expires to a gray unknown state, and keeps its own position, lock, and click-through settings.
- The traffic light shows one small light per agent session rather than a single large one. It stays within two rows by two columns at the width the vertical usage widget already occupies, centres a lone light on the second row, and pages through a carousel every five seconds past four sessions. Its frame, header, and interaction glyph now match the usage widget.
- Sessions can be told apart by their folder. Enabling "Show session folder" makes the helper add the leaf directory name of the session, never the path, and that name is shown on the light and kept in memory only. The option is off by default and rewrites its hooks when toggled.
- The Widget settings tab previews the traffic light beside the usage widget, using a fixed sample scene that never subscribes to the live activity pipe.
- Each light counts how long its current turn has been running, under the lamps. The duration is derived from the prompt timestamp already crossing the pipe, so it needs no new data from either agent: a turn already running at startup shows nothing rather than a guess, and a stale session stops counting. One shared clock ticks only while a turn is live and the widget is visible.
- The traffic light's header carries only the rings icon and the interaction glyph, matching the vertical usage widget. The application name and the aggregate state dot are gone: the widget is narrow, and each light already reports its own state.
- The tool badge on a light appears from two tools upward. A red light already means one tool is running, so a badge reading `1` only ever appeared where it added nothing.
- A session label too wide for its light slides between its two ends instead of being cut off, holding at each end so it can be read, and only while the light is on screen. With Windows animations off or high contrast on it is trimmed with an ellipsis and stays still.
- The tray menu splits its widget controls into one tab per widget. The traffic-light tab offers show, lock, click-through, and recover, a shortcut to its settings, and a read-only state row with the current color and session count. It appears only once a provider integration is enabled, so an install without one keeps the menu it had.
- Activity hooks are explicit opt-in integrations. Their helper pseudonymizes session and tool identifiers before sending a minimal event over a current-user named pipe; prompts, responses, transcripts, paths, tool input, and tool output never enter the application state, SQLite, history, or exports.

### Fixed

- A session whose agent stops without reporting an end no longer occupies the traffic light forever. Sessions are dropped once they outlive every freshness window, and each provider keeps at most 32 of them.
- The pending-update banner in the tray menu follows the interface language. Its text was formatted once, when the update arrived, and kept the language of that moment for the rest of the session. The same applies to the version and update copy in the About tab, which is now rebuilt when the language is applied.
- Claude Code no longer turns transient OAuth metadata into permanent quota columns. For example, Anthropic briefly returned an undocumented `nimbus_quill` object with `0%` utilization and no reset window; older builds stored it as an unknown `Q` quota and restored it after every restart. Unknown windowless OAuth fields now remain historical diagnostics instead of occupying the dashboard or widget, and new readings are accepted only for the published five-hour and seven-day quota families.

## 0.1.1 - 2026-08-08

### Fixed

- The Claude Code headline no longer shows the weekly percentage where the five-hour one belongs. Anthropic omits the reset instant while a window carries no usage yet, and a band with no reset used to lose its period, sort last, and hand the immediate slot to the weekly quota until the next request republished a reset. Window names this application understands now carry their own duration, so a five-hour window stays labelled and ordered as one from the moment it opens.
- An expired Claude session is reported instead of leaving the last percentage on screen as though it were current. The stored expiry is checked before polling, a rejected request is read as a session to renew rather than as a network failure, and the account is polled again as soon as Claude Code rewrites its credentials, so the reading returns when you come back instead of at some later poll.
- Missing Claude Code credentials are reported. The account poller used to skip in silence, leaving an absent account quota with nothing to explain it.
- One reading is stored once. Anthropic publishes the same reset instant as either the last second of a minute or the first of the next, which gave a single reading two identities and two rows in the history.

### Added

- Each provider card in the dashboard, and each widget tooltip, says why an adapter is in the state it reports: waiting for Claude Code activity, session expired, credentials missing, or account unreachable. The reasons follow the interface language.

## 0.1.0 - 2026-08-06

### Added

- Local-first Windows tray application for monitoring Codex and Claude Code usage.
- Live Codex integration through the local app server and Claude Code integration through OAuth usage data.
- Optional, reversible Claude Code `statusLine` bridge for session telemetry, whose installation backup of `settings.json` is removed once the uninstall round trip is verified.
- Guided first-run onboarding built on the dashboard chrome, with an English/Spanish toggle and English as the default for new installations.
- Always-on-top widget with activity-ring, horizontal-bar, and vertical-bar layouts.
- Quick-status popup, right-click tray controls, full dashboard, and configurable widget behavior.
- SQLite usage history with provider and date filters, analytics, and CSV or JSON export. Session duration is recorded once per whole minute to keep the history compact.
- Resilient Claude Code telemetry: the account-usage poller survives network timeouts, the status line listener survives named-pipe failures and accepts several concurrent sessions, and bridge and account health are reported independently so an idle status line never masks live account quotas.
- Status line payloads read as UTF-8, with the user's previous rendering preserved when a payload exceeds the bridge size limit.
- Quota meters that follow the reported percentage instead of collapsing to zero on a stale reading; staleness is shown by dimming the widget band and by the freshness text.
- Distinct labelling for the account-wide `seven_day_oauth_apps` quota, so it is not shown as a second unnamed weekly window.
- English and Spanish interfaces with light, dark, system, and high-contrast support.
- Widget locking, click-through mode, multi-monitor placement, and `Ctrl+Shift+U` recovery.
- Automated unit, integration, adapter-contract, accessibility, and Windows quality-matrix checks.
- Professional English documentation, real application screenshots, and privacy-safe promotional artwork.
- Windows installer published on GitHub Releases for x64 and ARM64. It installs for the current user, so no administrator prompt appears, and each architecture updates only from packages built for it.
- Consented application updates. AI Vitals checks the published releases, downloads a new version in the background, and installs it only when asked; automatic checking can be switched off, and pre-releases are never offered to stable installations.
- An About section showing the installed version and update channel, with a manual check, the automatic-check preference, and an opt-in "start with Windows" that is disabled by default.
- Uninstalling restores the Claude Code `statusLine` to what it was, removes the startup entry, and keeps local usage history and settings.

### Changed

- The Claude Code status line helper now runs from a fixed location outside the versioned application directory, so the path stored in Claude Code's settings survives updates. An existing installation is moved there automatically on the first start.

### Security

- Kept usage data, settings, and history on the local machine without application telemetry.
- Published a SHA256 checksum for every release asset. Releases are not code signed yet, so Windows SmartScreen warns on first run until a certificate is in place.
- Excluded prompts, responses, transcripts, project paths, provider credentials, and raw session identifiers from storage.
- Added local HMAC pseudonymization for Claude Code session identifiers and allowlisted payload extraction.
