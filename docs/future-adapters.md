# Provider Adapter Reference

Working guidance for adding provider adapters without repeating the consistency problems found while implementing Claude Code. The priority is to display real quotas and clearly state whether a value is exact, estimated, or unavailable.

## Shared rules

1. Prefer data sources in this order: account quota endpoint, structured client state, structured local logs, and estimation only as a last resort.
2. Reload credentials on every poll. Some clients rotate tokens while running, so an adapter must not capture one only at construction time.
3. Treat failures, incomplete responses, and missing windows as unknown data. Never publish `0` unless the source explicitly reported zero.
4. Preserve every window published by the provider and deduplicate by canonical identity (`provider + window name`), not source copy.
5. Ignore responses older than the latest accepted observation. Apply timeouts, cancellation, and backoff with jitter while retaining the last valid value during transient failures.
6. Mark every observation as exact or estimated and record only technical metadata. Never persist tokens, prompts, responses, or project paths.
7. Keep account quotas separate from session telemetry. Tokens and local process cost may come from logs or `statusLine`, but they must not replace global limits.

## Claude Code reference implementation

The two implementations reviewed query `https://api.anthropic.com/api/oauth/usage` with the token from `~/.claude/.credentials.json` (`claudeAiOauth.accessToken`) and the `oauth-2025-04-20` beta header:

- [juliantanx/aiusage — quota.ts](https://github.com/juliantanx/aiusage/blob/main/packages/cli/src/quota.ts)
- [rygel/AIUsageTracker — ClaudeCodeProvider.cs](https://github.com/rygel/AIUsageTracker/blob/main/AIUsageTracker.Infrastructure/Providers/ClaudeCodeProvider.cs)

The response may contain `five_hour`, `seven_day`, `seven_day_sonnet`, `seven_day_opus`, and future windows. Iterate over objects containing `utilization` instead of limiting the parser to two names. Reload the token on every refresh because Claude Code may rotate it during a session.

In AI Vitals, the OAuth endpoint is authoritative for quotas. `statusLine` remains a fallback and a source of session token/cost data. A failed poll keeps the last valid value and never creates a zero-valued observation.

## Future candidates

| Priority | Provider | Proposed source | Windows/data | Notes |
| --- | --- | --- | --- | --- |
| High | GitHub Copilot | `https://api.github.com/copilot_internal/user` with the local Copilot/GitHub session | Plan-dependent chat and completion quotas | Normalize `remaining` and `entitlement` to percentage used. Revalidate credential formats for every IDE. |
| High | Gemini CLI | Local OAuth from `~/.gemini/oauth_creds.json`; refresh through `oauth2.googleapis.com/token`; quotas from `cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota` | Per-model quotas and reset times | Keep model families separate. The endpoint is internal and requires fixture-based contract tests. |
| Medium | OpenRouter | `https://openrouter.ai/api/v1/credits` and `/api/v1/key` | Used/remaining credit and key limit | Exact account source, but requires a user-provided API key. |
| Medium | OpenCode | `https://api.opencode.ai/v1/credits`; key from local OpenCode `auth.json` files | Used/total credits | Discover native Windows, Linux, and macOS paths without copying credentials into app storage. |
| Medium | Codex | `https://chatgpt.com/backend-api/wham/usage` with `~/.codex/auth.json` | Primary and secondary windows | An adapter already exists; apply the same credential reload, deduplication, and last-valid-value pattern used for Claude. |
| Low | Billing-API-only providers | Official endpoint with an explicit key | Credits, spend, or budget | Do not confuse historical spend with a time-based quota. Obtain consent before storing a key. |
| Low | Clients without a quota API | Local SQLite/JSONL, read-only | Estimated tokens and cost | Label values as estimated; detect log rotation/truncation and checkpoint by file/inode. |

Additional implementation references:

- [juliantanx/aiusage](https://github.com/juliantanx/aiusage), reviewed at `68aeeac1044191fd0c7fd24a065930b9603a789d`.
- [rygel/AIUsageTracker](https://github.com/rygel/AIUsageTracker), reviewed at `d011abfa1f7d381f0d7971d2a93b988adaac46e5`.
- [Rygel — GitHubCopilotProvider.cs](https://github.com/rygel/AIUsageTracker/blob/main/AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs).
- [Rygel — GeminiProvider.cs](https://github.com/rygel/AIUsageTracker/blob/main/AIUsageTracker.Infrastructure/Providers/GeminiProvider.cs).
- [Rygel — OpenRouterProvider.cs](https://github.com/rygel/AIUsageTracker/blob/main/AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs).
- [Rygel — OpenCodeProvider.cs](https://github.com/rygel/AIUsageTracker/blob/main/AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs).

## Minimum adapter test contract

- A fixture containing every known window and another containing an unknown window.
- Credential rotation between two polls.
- HTTP 401, 429, 5xx, timeout, and incomplete JSON without resetting the previous value.
- Out-of-order responses and two sources publishing the same window.
- A real server-reported transition to zero.
- Clean watcher cancellation and no secrets in logs or SQLite.
- A manual test with the provider client active for at least one token-rotation cycle.
