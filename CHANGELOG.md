# Changelog

All notable changes to AI Vitals are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
