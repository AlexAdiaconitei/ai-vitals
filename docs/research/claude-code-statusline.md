# Claude Code `statusLine` Contract for Phase 02

- Verification date: 2026-08-05
- Scope: Claude Code on Windows, with a local and reversible AI Vitals integration
- Sources: Anthropic's official documentation and changelog, plus a local CLI version check

## Executive summary

`statusLine` is currently the appropriate official local interface for receiving metrics from an interactive Claude Code session. Claude Code runs one shell command, sends a complete JSON object to its `stdin`, and renders the command's `stdout` in the UI. Configuration supports one `statusLine` object; built-in multiplexing, composition, and reversible uninstallation do not exist. See the [official `statusLine` documentation](https://code.claude.com/docs/en/statusline).

For Phase 02, install a user-level local wrapper with explicit consent. It should:

1. Read the `stdin` JSON exactly once.
2. Extract only allowed metrics and deliver them locally to AI Vitals.
3. If a compatible status line already exists, run its command with an identical copy of the JSON and reproduce its `stdout`.
4. Never block the status line when AI Vitals is unavailable.
5. Restore the exact previous configuration on disconnect, but only if the current configuration still matches the one installed by AI Vitals.

Four product limitations must remain visible:

- `rate_limits` is documented only for compatible Claude.ai Pro/Max subscribers and appears after the first response. Each window may be absent independently.
- Project or managed configuration takes precedence over user configuration and may prevent the global integration from running.
- Claude Code does not document a fixed `statusLine` timeout. Slow processes block an update until they finish or a new event cancels them.
- The JSON includes paths and other sensitive metadata in addition to metrics. Claude Code does not provide field projection, so the wrapper is responsible for minimization.

## 1. Configuration and command

Manual configuration can live in `~/.claude/settings.json` for the user or in project settings. The documented contract is an object of type `command`; `command` may be a script path or an inline shell command. See [manual configuration](https://code.claude.com/docs/en/statusline#manually-configure-a-status-line).

```json
{
  "statusLine": {
    "type": "command",
    "command": "~/.claude/statusline.sh",
    "padding": 2,
    "refreshInterval": 5,
    "hideVimModeIndicator": true
  }
}
```

Documented fields:

| Field | Required | Meaning |
| --- | ---: | --- |
| `type` | Yes | Currently, `"command"`. |
| `command` | Yes | Script path or inline shell command. |
| `padding` | No | Additional horizontal indentation in characters; defaults to `0`. |
| `refreshInterval` | No | Repeats the command every N seconds in addition to event triggers; minimum `1`. Without it, updates are event-only. |
| `hideVimModeIndicator` | No | When `true`, hides the built-in Vim indicator if the script already renders `vim.mode`. |

The `/statusline` command accepts natural-language instructions, generates a script under `~/.claude/`, and updates settings. It can also remove the configuration. A product integration should not use it as an automatic installer because its generated output is not a stable composition protocol. Direct, transactional, verifiable settings updates are easier to control. See [`/statusline` usage](https://code.claude.com/docs/en/statusline#use-the-statusline-command).

Claude Code reloads settings automatically, although visible changes wait for the next update trigger. See the [update lifecycle](https://code.claude.com/docs/en/statusline#how-status-lines-work).

### Scopes and precedence

Relevant locations:

| Scope | Path | Purpose |
| --- | --- | --- |
| User | `~/.claude/settings.json` | Global for the user. On Windows, `~` is the user profile. |
| Shared project | `.claude/settings.json` | Repository configuration, usually version-controlled. |
| Local project | `.claude/settings.local.json` | Personal repository override. |
| Managed | Policy, MDM, registry, or managed settings | Organizational control. |

Precedence from highest to lowest is managed settings, command arguments/`--settings`, local project, shared project, and user. A higher-priority `statusLine` value replaces the lower one; special cross-scope merging is documented only for arrays. See the official [settings precedence](https://code.claude.com/docs/en/settings#settings-precedence).

Therefore, installing the wrapper in user settings provides global coverage only where no higher scope defines `statusLine`. The adapter should report `Degraded/Overridden` for those sessions instead of presenting zero usage. `/status` shows which settings sources were loaded, but not which layer supplied each individual key. See [active settings verification](https://code.claude.com/docs/en/settings#verify-active-settings).

## 2. Execution, frequency, and output

Claude Code passes one JSON object through `stdin`. The command reads it, and its `stdout` becomes the status line. Multiple lines, ANSI sequences, and OSC 8 links are supported when the terminal supports them. The custom line occupies its own row and does not replace built-in badges. It runs locally and consumes no API tokens. See [how status lines work](https://code.claude.com/docs/en/statusline#how-status-lines-work).

Documented triggers:

- once when a session starts, including resumed sessions;
- when a new assistant message arrives;
- when `/compact` finishes;
- when permission mode changes;
- when Vim mode changes;
- when `refreshInterval` expires, if configured.

Updates are grouped with a 300 ms debounce. If a new trigger arrives while the command is still running, Claude Code cancels the current process. Before v2.1.216, resuming a session could run the command twice in succession. Events may stop while the main session is idle; `refreshInterval` is the only documented periodic trigger. See [frequency and cancellation](https://code.claude.com/docs/en/statusline#how-status-lines-work) and the [v2.1.216 change](https://code.claude.com/docs/en/changelog#21216).

No maximum `statusLine` timeout is documented. The guidance states only that a slow script blocks updates, a later trigger cancels the running process, and empty output or a non-zero exit code leaves the line blank. Consequently:

- do not reuse the timeout documented for hooks; it is a different contract;
- delivery to AI Vitals must be local, bounded, and fail-open;
- do not wait for the resident application to confirm persistence;
- preserve the Claude Code experience first and treat observation as best-effort.

`disableAllHooks: true` also disables `statusLine`. The command runs only after workspace trust is accepted for the current directory. Errors, missing trust, or safe mode may produce a blank line and no events; this means unknown health, not zero usage. See the official [troubleshooting guide](https://code.claude.com/docs/en/statusline#troubleshooting).

## 3. Relevant input JSON

The following is the subset required by the adapter. The complete object contains more fields and the parser must tolerate unknown properties. See the [schema and available fields](https://code.claude.com/docs/en/statusline#available-data).

```json
{
  "session_id": "abc123...",
  "session_name": "my-session",
  "model": {
    "id": "claude-opus-5",
    "display_name": "Opus"
  },
  "version": "2.1.222",
  "cost": {
    "total_cost_usd": 0.01234,
    "total_duration_ms": 45000,
    "total_api_duration_ms": 2300,
    "total_lines_added": 156,
    "total_lines_removed": 23
  },
  "context_window": {
    "total_input_tokens": 15500,
    "total_output_tokens": 1200,
    "context_window_size": 200000,
    "used_percentage": 8,
    "remaining_percentage": 92,
    "current_usage": {
      "input_tokens": 8500,
      "output_tokens": 1200,
      "cache_creation_input_tokens": 5000,
      "cache_read_input_tokens": 2000
    }
  },
  "rate_limits": {
    "five_hour": {
      "used_percentage": 23.5,
      "resets_at": 1738425600
    },
    "seven_day": {
      "used_percentage": 41.2,
      "resets_at": 1738857600
    }
  }
}
```

### Session and model

| Field | Meaning | Proposed handling |
| --- | --- | --- |
| `session_id` | Unique session identifier. | Required for session granularity; persist only a deterministic pseudonymous identifier, never the source value. |
| `session_name` | Custom name or generated title; may be absent. | Do not ingest. It may reveal work content or intent and is unnecessary. |
| `model.id` | Current model identifier. | Ingest as a model dimension and preserve unknown future values. |
| `model.display_name` | Display name for the model. | Optional; use for presentation, never logic. |
| `version` | Claude Code version. | Ingest for compatibility and redacted diagnostics. |

### Cost

`cost.total_cost_usd` is a client-calculated session-cost estimate. It may differ from the actual bill and resets when `/clear` starts a new session. `total_duration_ms` measures wall-clock time; `total_api_duration_ms` measures time waiting for API responses. `total_lines_added/removed` counts changed lines. See [cost semantics](https://code.claude.com/docs/en/statusline#available-data).

AI Vitals may store `total_cost_usd` as an observed metric with `Estimated` quality, never as billing or subscription consumption. On Pro/Max plans, session cost is not quota usage. Do not use negative differences between sessions or sum repeated snapshots: this is a cumulative per-session counter and must be projected by identity and timestamp.

### Tokens and context

- Since v2.1.132, `total_input_tokens` and `total_output_tokens` represent tokens currently in the context window according to the latest API response. Previously, they were session totals. See the [v2.1.132 semantic change](https://code.claude.com/docs/en/changelog#21132).
- `total_input_tokens` includes `input_tokens`, `cache_creation_input_tokens`, and `cache_read_input_tokens`; `total_output_tokens` is the latest response output.
- Both totals are `0` before the first response. This does not mean an unused session and must remain an unobserved state.
- `current_usage` is `null` before the first call and returns to `null` after `/compact` until the next call.
- `used_percentage` and `remaining_percentage` may initially be `null`.
- `used_percentage` uses input and cache tokens only, not output. Consume the official percentage rather than recalculating it.
- `context_window_size` is commonly 200,000 or 1,000,000 depending on the model and extended context. Never hard-code a fixed capacity.

These details are part of the official [context-window field documentation](https://code.claude.com/docs/en/statusline#context-window-fields).

### Five-hour and seven-day rate limits

| Field | Meaning |
| --- | --- |
| `rate_limits.five_hour.used_percentage` | Percentage consumed from the rolling five-hour window, 0–100. |
| `rate_limits.five_hour.resets_at` | Reset time in Unix epoch seconds. |
| `rate_limits.seven_day.used_percentage` | Percentage consumed from the weekly window, 0–100. |
| `rate_limits.seven_day.resets_at` | Reset time in Unix epoch seconds. |

`rate_limits` is documented only for compatible Claude.ai Pro/Max subscribers and only after the first API response. The whole object and each individual window may be absent independently. Do not infer `0`, fabricate reset dates, or interpret absence as logout without additional evidence. See [rate-limit usage](https://code.claude.com/docs/en/statusline#rate-limit-usage).

The field was added in v2.1.80, which is the minimum functional version for quotas. Version 2.1.132 is the recommended minimum to avoid the historical token ambiguity. See the [v2.1.80 changelog](https://code.claude.com/docs/en/changelog#2180).

## 4. Windows

On Windows, Claude Code executes the command through Git Bash when installed, or PowerShell otherwise. Git Bash treats unquoted backslashes as escapes, so `command` paths should use `/`. `~` also works and expands to the Windows home directory. Anthropic recommends invoking `powershell` explicitly for PowerShell scripts, which works with either outer shell. See the official [Windows configuration](https://code.claude.com/docs/en/statusline#windows-configuration).

```json
{
  "statusLine": {
    "type": "command",
    "command": "powershell -NoProfile -File C:/Users/username/.claude/statusline.ps1"
  }
}
```

Implications:

- generate absolute paths with `/` and quote paths containing spaces correctly;
- do not assume `pwsh`; the official example uses Windows PowerShell, `powershell`;
- avoid complex inline commands whose quoting differs between Bash and PowerShell;
- prefer a small launcher with simple arguments and a script or executable in a controlled path;
- test with and without Git Bash, with a profile path containing spaces, and under a hardened PowerShell execution policy;
- do not depend on an undocumented minimum PowerShell version or quoting behavior.

## 5. Preserving and composing an existing status line

The documentation defines one `statusLine` object. It does not define handler lists, multiplexing, chaining, ownership markers, backups, uninstall behavior, or transactions. Any composition is an AI Vitals design choice, not an Anthropic guarantee.

### Recommended reversible installation

1. Ask for consent and show the exact path that will change and whether a previous status line exists.
2. Read `settings.json` as bytes and validate the JSON before changing it.
3. Save the following in protected application storage:
   - the exact bytes of the original file;
   - the exact previous `statusLine` JSON object, or an absence marker;
   - the hash of the original file;
   - the exact installed object and an installation ID.
4. Update only `statusLine`, preserving every other key; use a temporary file, flush, and atomic replace.
5. Read back and validate the result before reporting a connected state.
6. On uninstall, restore the previous object only if the current value exactly matches the installed value. If it changed, do not overwrite it; report a conflict and offer manual repair.
7. Keep the backup until the disconnect round trip has been verified.

Claude Code creates its own configuration backups, but those do not replace a transactional, installation-specific product backup. See [settings files](https://code.claude.com/docs/en/settings#settings-files).

### Composing the previous command

A wrapper must read all of `stdin` once and duplicate the same bytes to two consumers:

- the local metrics collector, which fails open and writes no `stdout`;
- the previous command, whose `stdout`, visible `stderr`, and exit code should be preserved where possible.

Two commands cannot sequentially read the same pipe unless the payload is buffered first. The wrapper must not reserialize the JSON before passing it to the previous command; a semantically equivalent document could still break a consumer that depends on bytes, ordering, or whitespace.

On Windows, an arbitrary previous command may depend on Claude Code's selected shell. Reproducing its exact invocation and quoting from another process is undocumented. Automatic composition should therefore initially support only recognizable safe forms: direct paths to `.ps1`, `.sh`, `.py`, `.js`, or executables with simple arguments. For a complex inline command, leave settings untouched and request manual composition; never silently replace it.

### Scope conflicts

The global installer must edit only user settings. It must not mutate repository `.claude/settings.json` files or managed settings. A project's `statusLine` takes precedence and may prevent the integration from receiving events in that project. Present this as a diagnostic limitation instead of silently expanding the write scope.

## 6. Privacy and security

The complete JSON includes metadata that AI Vitals does not need:

- `cwd` and paths under `workspace`, including added folders and worktrees;
- repository identity derived from `origin`;
- `transcript_path`;
- `session_name` and `prompt_id`;
- agent name, pull request, and other UI metadata.

The documented schema contains no prompt text or message content, but paths, session names, repository identity, and transcript paths may reveal the user's work. See the [complete schema](https://code.claude.com/docs/en/statusline#available-data).

Minimum policy:

- parse only `version`, `session_id`, `model`, `cost`, `context_window`, and `rate_limits` in the wrapper;
- discard the raw JSON document immediately after allowlisted fields are extracted;
- never read the file referenced by `transcript_path`;
- never log payloads, paths, session names, prompt IDs, repositories, previous-command `stdout`/`stderr`, or environment variables;
- pseudonymize `session_id` with HMAC and a local key, or equivalent, before persistence; a plain hash permits external correlation if the ID leaks;
- use bounded, local-only IPC authenticated or authorized for the current user;
- perform no network access from the wrapper and introduce no credentials;
- keep the latest snapshot and mark it stale on failure; never turn a failure into zero.

Local execution does not imply sandboxing: this is an arbitrary shell command running as the user. Anthropic requires workspace trust, and `disableAllHooks` can disable it, but the documentation does not promise network/filesystem isolation, retention limits, or protection for data that an external script stores. See [workspace trust and errors](https://code.claude.com/docs/en/statusline#troubleshooting).

## 7. Versions, missing fields, and compatibility

Milestones from the official changelog:

| Version | Relevant change |
| --- | --- |
| 1.0.71 | Introduced `/statusline`. |
| 1.0.85 | Added session cost to input. |
| 1.0.88 | Added `exceeds_200k_tokens`. |
| 1.0.90 | Applied settings changes immediately. |
| 2.0.70 | Added `context_window.current_usage`. |
| 2.1.80 | Added five-hour and seven-day `rate_limits`. |
| 2.1.132 | Changed token totals from session accumulation to current context. |
| 2.1.153 | Exposed `COLUMNS` and `LINES` for output sizing. |
| 2.1.196 | Added `prompt_id`. |
| 2.1.216 | Fixed duplicate execution when resuming. |

Source: the official [Claude Code changelog](https://code.claude.com/docs/en/changelog).

On 2026-08-05, `claude --version` on the development machine returned `2.1.222 (Claude Code)`, which includes the corrected quota and token contracts described above. This local observation must not become a product requirement; the adapter must discover the installed version on every machine.

Parser and compatibility rules:

- accept unknown future properties;
- treat objects, windows, and scalar fields as optional, except `session_id` when session granularity is required;
- distinguish `missing`, `null`, `0`, valid values, and out-of-range values;
- validate finite percentages in `[0, 100]` and representable epoch values, degrading only the invalid metric;
- do not reject the whole event because an unused field is new or malformed;
- version fixtures for at least pre-2.1.80, 2.1.80–2.1.131, 2.1.132+, partial payload, post-compact `null`, and a future payload with unknown fields;
- deduplicate repeated invocations by stable content, session, and timestamp/window without assuming exactly-once delivery;
- if the version cannot be parsed, use the observed structural contract and mark quality as `UnknownVersion` instead of breaking the app.

## 8. Phase 02 decisions

1. **Minimum version for full connection:** Claude Code 2.1.132. Versions 2.1.80–2.1.131 may expose quotas but use historical token semantics; support them only with explicit capability/quality. Before 2.1.80, connect without quotas or report partial compatibility.
2. **Installation scope:** user settings only, with an explanation of project and managed overrides.
3. **Refresh:** `refreshInterval` is set to 30 seconds, well above the one-second minimum. Quotas themselves change only after a response, but the periodic trigger is what keeps freshness and health observable while a session sits idle. The cost is that every tick replays a payload whose monotonic counters have moved, so cumulative session metrics must be throttled before they are persisted rather than written per tick.
4. **IPC:** one-way, local, non-blocking writes; the wrapper must exit quickly even when AI Vitals is closed. The resident side must accept several connections at once: Claude Code runs one status line process per session, and a single listener silently drops the payloads it cannot answer in time.
5. **Composition:** automatic only for recognizable, verifiable previous commands. Complex cases require intervention, never overwrite.
6. **Reversibility:** exact backup, compare-before-restore, and a round-trip test.
7. **Data truth:** absence, `null`, and failures map to `Unavailable` or `Stale`; no case creates a synthetic zero.
8. **Privacy:** strict allowlist and immediate disposal of raw JSON; never store paths, transcript details, prompt/session names, or repository identity.

## Primary sources

- Anthropic, [Customize your status line](https://code.claude.com/docs/en/statusline).
- Anthropic, [Claude Code settings](https://code.claude.com/docs/en/settings).
- Anthropic, [Claude Code changelog](https://code.claude.com/docs/en/changelog).
- Anthropic, [Environment variables](https://code.claude.com/docs/en/env-vars) — process/PowerShell behavior and modes that disable customizations.
