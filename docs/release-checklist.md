# Release Checklist

Status on 2026-09-05: **2 of 8 checks executed on x64, 0 on ARM64, none on a clean machine**.
Checks 5 and 8 were run against `0.2.0-beta.1` on a development machine that had carried earlier
builds. That is weaker evidence than the clean virtual machine this list asks for, and it is
recorded as such rather than counted as a clean-machine pass. The release workflow runs the build,
the Claude Code bridge verification, and the automated test suite before it publishes anything. It
does not install, update, roll back, or uninstall AI Vitals. Those remain human checks, and this
document is where their outcome is recorded.

Run the whole list on a clean Windows virtual machine before tagging a stable version. A pre-release
tag (`v0.1.1-beta.1`) exists precisely so the pipeline and these checks can be rehearsed without
offering anything to stable installations.

## Publish a version

1. Write the version's section in `CHANGELOG.md`, using the same `## <version> - <date>` heading
   style as the previous entries. The release workflow fails before building if it is missing or
   empty. A pre-release reuses the section of the version it leads to, so `v0.1.0-beta.1` publishes
   the notes written under `## 0.1.0`.
2. Rehearse: `git tag v<version>-beta.1 && git push origin v<version>-beta.1`. Confirm the workflow
   publishes a GitHub pre-release with both installers, the checksum table, and the changelog notes.
3. Run the checks below against that pre-release.
4. Tag the stable version: `git tag v<version> && git push origin v<version>`.
5. Confirm the release page shows both architectures, `SHA256SUMS.txt`, and the composed notes.

To rehearse without publishing, run the workflow manually from the Actions tab with **publish**
unchecked: it builds both installers and attaches them to the workflow run.

## Clean-machine checks

| # | Check | Expected result | x64 | ARM64 |
| --: | --- | --- | :-: | :-: |
| 1 | Install | `AIVitalsApp-win-<arch>-Setup.exe` completes without an administrator prompt, and AI Vitals starts into the tray. | Pending | Pending |
| 2 | Checksum | `Get-FileHash` of the downloaded installer matches the release notes and `SHA256SUMS.txt`. | Pending | Pending |
| 3 | First run | Onboarding appears, both providers connect, and the About section shows the installed version and the `win-<arch>` channel. | Pending | Pending |
| 4 | Start with Windows | Enabling the option adds a `HKCU\...\Run\AIVitals` entry pointing at `%LOCALAPPDATA%\AIVitalsApp\AIVitals.App.exe`, and AI Vitals starts after signing out and back in. | Pending | Pending |
| 5 | Update | Installing the previous version, then checking for updates, offers the new one, downloads it, and installs it only after **Install and restart**. Usage history and preferences survive. | 2026-09-05 · 0.2.0-beta.1 (dev machine) | Pending |
| 6 | Update while the bridge is live | Applying an update with Claude Code open leaves the `statusLine` working and still pointing at `%LOCALAPPDATA%\AIVitals\bridge`, and leaves the activity hooks in both agents pointing at a helper that still exists. | Pending | Pending |
| 7 | Rollback | Reinstalling the previous version over the new one opens the existing database without data loss. | Pending | Pending |
| 8 | Uninstall | Uninstalling restores the previous `statusLine` in `~/.claude/settings.json`, removes every AI Vitals activity hook from `~/.claude/settings.json` and `~/.codex/hooks.json` while leaving hooks owned by anything else untouched, removes the Run entry and `%LOCALAPPDATA%\AIVitalsApp`, and keeps `%LOCALAPPDATA%\AIVitals`. | 2026-09-05 · 0.2.0-beta.1 (dev machine) | Pending |

The traffic light itself was exercised on `0.2.0-beta.1`: hooks accepted in the Codex CLI, and the
lights reacting to live Claude Code and Codex sessions. That is not one of the eight checks, but it
is the first end-to-end confirmation that the feature works outside a development build.

Record the date and build for every cell that passes, the same way
[quality-matrix.md](quality-matrix.md) does. A cell that was not executed stays `Pending`; a
successful build is not evidence that an install works.

## Known gaps

- **Code signing.** Releases are unsigned, so SmartScreen warns on first run. The workflow already
  has the signing step wired behind the `WINDOWS_SIGN_PARAMS` secret: adding the secret enables it
  with no code change.
- **ARM64 automation.** GitHub-hosted Windows runners are x64, so the ARM64 installer is built but
  never executed by CI. Its column above can only be filled in by hand.
- **No SBOM or dependency scan.** The release workflow does not generate an SBOM and does not fail
  on vulnerable packages. Run `dotnet list package --vulnerable --include-transitive` manually
  before a stable tag.
