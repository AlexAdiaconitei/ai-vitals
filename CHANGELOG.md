# Changelog

All notable changes to AI Vitals are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 0.2.0 - 2026-09-05

### Added

- An activity traffic light, a second always-on-top widget that shows what your agents are doing right now. Green is a turn that ended, yellow a turn being processed, red at least one tool running, and grey no reliable signal. It starts grey after every restart and returns to grey when a session goes quiet, because a stale reading must never pass for an idle agent. It keeps its own position, lock and click-through settings, and `Ctrl+Shift+U` recovers it alongside the quota widget.
- The traffic light shows one light per agent session, so several agents at once stay legible. It holds at most two rows by two columns at the width the vertical quota widget already occupies: one session is a single narrow light, three place the odd one centred on the second row, and past four the widget pages through them every five seconds instead of growing, pausing while the pointer rests on it. Lights keep their place when their state changes, and a session that ends without saying so disappears once it outlives every freshness window.
- Each light times the turn it is running, under the lamps. The duration is measured from the prompt AI Vitals saw, so a turn already under way when the application starts reads as a lower bound with a leading `~` rather than passing for an exact time, and a session that goes quiet stops counting. A count beside a light appears from two tools upward, where it reports work happening in parallel that the colour alone cannot.
- Sessions can be told apart by their folder. **Show session folder** is off by default; enabling it labels each light with the last directory name of its session, never the path. A label too wide for its light slides between its two ends so the whole name can be read, and trims to an ellipsis instead when Windows animations are off or high contrast is on.
- Claude Code and Codex feed the traffic light through opt-in lifecycle hooks, enabled independently and only after explicit confirmation. Codex additionally requires the new hooks to be reviewed in its `/hooks` screen. Installation is additive and reversible: hooks written by anything else are left untouched, and uninstalling AI Vitals removes only its own.
- The tray menu splits its widget controls into one tab per widget. The traffic-light tab carries show, lock, click-through and recover, a shortcut to its settings, and a read-only row with the current state and session count. It appears only once a provider integration is enabled, so an installation without one keeps the menu it had.
- The Widget settings tab previews the traffic light beside the quota widget, using a fixed sample scene that never subscribes to live activity.
- A waiting update is announced where the application is actually visible: the tray icon carries a dot, a system notification is raised once per version, the left-click quick view carries the same install entry as the right-click menu, and the dashboard's About tab shows a dot in its navigation entry.

### Fixed

- The pending-update banner in the tray menu follows the interface language. Its text was formatted once, when the update arrived, and kept the language of that moment for the rest of the session. The same applies to the version and update copy in the About tab, which is now rebuilt when the language is applied.
- Claude Code no longer turns transient OAuth metadata into permanent quota columns. For example, Anthropic briefly returned an undocumented `nimbus_quill` object with `0%` utilization and no reset window; older builds stored it as an unknown `Q` quota and restored it after every restart. Unknown windowless OAuth fields now remain historical diagnostics instead of occupying the dashboard or widget, and new readings are accepted only for the published five-hour and seven-day quota families.

### Security

- Activity never leaves the machine and never reaches storage. The local hook helper reads only a session identifier, a tool identifier and the event type, pseudonymises both identifiers with HMAC before they cross a named pipe restricted to the current user, and discards prompts, responses, transcripts, paths, tool input and tool output. Traffic-light state lives in memory alone: it is never written to SQLite, history or exports, and it is gone when the application closes. The optional session folder is the only agent-supplied text that is ever displayed, is reduced to a single directory name, and is rejected at the pipe if it carries a path separator or a control character.

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
