# AI Usage Monitoring Domain Language

This context defines how AI Vitals represents observable AI-assistant consumption without combining incompatible quotas or storing work content.

## Sources and connections

**Provider**

An external AI product whose usage is monitored, such as Codex or Claude Code.

_Avoid:_ model, account, adapter.

**Connection**

A configured local link to a provider. The MVP supports at most one active connection per provider.

_Avoid:_ account, login, integration.

**Adapter**

A module that translates a connection's native data into the application's shared language.

_Avoid:_ provider, plugin.

**Capability**

A data category an adapter can provide reliably, such as quotas, tokens, cost, sessions, or health.

_Avoid:_ feature, implicitly available metric.

## Observed usage

**Usage observation**

An immutable measurement of one capability at a point in time, with explicit provenance and quality.

_Avoid:_ prediction, unlabelled estimate.

**Quota window**

A provider-defined period containing usage, remaining percentage, and a reset time when available.

_Avoid:_ global limit, balance.

**Quota band**

The presentation role of a quota window within one provider. A band retains its own percentage and reset time and is never the sum of other windows.

_Avoid:_ global quota, quota average.

**Immediate band**

The shortest published window, such as five hours or one day, which may block provider use first. If the provider publishes only a long window, this band does not exist.

_Avoid:_ primary quota, current consumption.

**Total band**

The longest published window applicable to the plan, such as a week or month. It is not an arithmetic total and does not include the immediate band.

_Avoid:_ sum, global quota.

**Inactive window**

A window whose reset time has passed. Its historical observation remains stored, but its percentage is not presented as current consumption. This is different from converting a read failure to zero.

_Avoid:_ stale data, disconnected provider.

**Data quality**

A visible classification of an observation as exact, estimated, or unavailable.

_Avoid:_ implicit confidence.

**Freshness**

The relationship between an observation timestamp and its adapter's staleness threshold.

_Avoid:_ provider status.

A stale quota observation may retain its last percentage for diagnostics, but it does not occupy a bar or appear as current consumption. The UI labels it as the last known value and explains what may produce a new reading.

**Anonymous session**

A local activity interval associated with a provider and model through an internal identifier, without prompts, responses, paths, or project names.

_Avoid:_ conversation, project.

**Daily aggregate**

A permanent summary of detailed observations that have exceeded the 30-day retention period.

_Avoid:_ historical event.

## Presentation

**Quota widget**

The compact, always-on-top window that shows one to four pinned quota connections in a shared visual mode.

_Avoid:_ dashboard, popup.

**Visual mode**

The widget representation: activity-ring grid, parallel horizontal bars, or parallel vertical bars.

_Avoid:_ semáforo, modo de actividad.

**Activity traffic light**

The independent, memory-only always-on-top widget that reduces opt-in Claude Code and Codex lifecycle hooks to unknown, idle, processing, or tool-running.

_Avoid:_ quota widget, persisted observation, activity ring.

**Session light**

One traffic light inside the activity widget, standing for a single agent session, or for an enabled provider that has not reported yet.

_Avoid:_ lamp, provider light.

**Session label**

The leaf directory name of an agent session, shown on its light only when the user opted in. Never a path, never persisted. A label wider than its light slides between its ends rather than being cut.

_Avoid:_ working directory, project path, session name.

**Activity carousel**

The paging the activity widget falls back to past four session lights, so it never grows beyond two rows by two columns.

_Avoid:_ scroll, animation, slideshow.

**Quick popup**

The rich view shared by left-clicking either the widget or the tray icon.

_Avoid:_ context menu, dashboard.

**Dashboard**

The resizable main window for analysis, history, exports, adapters, and settings.

_Avoid:_ popup, widget.

**Visual state**

A stable signal expressed through color, icon, and text or pattern, without system notifications, motion, or geometry changes. Two motions are sanctioned exceptions, and neither carries state: the activity carousel between pages, and a sliding label revealing a name too long for its light.

_Avoid:_ predictive alert, toast.
