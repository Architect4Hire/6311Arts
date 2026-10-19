# 0015 — Claude Code hooks are bash scripts (Git Bash on Windows)

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/settings.json` (hooks), `.claude/hooks/secret-guard.sh`, `.claude/hooks/format.sh`, `.claude/README.md`

## Context

The toolkit has two hooks: `secret-guard.sh` (PreToolUse, blocks credential material) and `format.sh` (PostToolUse, runs `dotnet format` and the workspace-pinned prettier). The main development machine is Windows 11, where the default shell is PowerShell. CI and any later contributors may use Linux or macOS. Claude Code runs hook commands under Git Bash on Windows, or under PowerShell when Git Bash isn't installed ([hooks docs](https://code.claude.com/docs/en/hooks)).

## Decision

- **Keep the hooks as bash scripts, one implementation for every platform.** On Windows they run under **Git Bash**, which is a prerequisite.
- **Hook commands reference `"${CLAUDE_PROJECT_DIR}"/.claude/hooks/...`** so they resolve from any working directory. Never add a bare `bash` prefix: on this machine `bash` resolves to the WSL launcher.
- **The secret guard sets `"onFailure": "block"`** (Claude Code ≥ 2.1.295), so a guard that can't run blocks the write instead of letting it through.
- **The hook scripts are committed with mode `100755`.**

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Bash only, Git Bash on Windows (chosen)** | One script per hook, tested once. Needs Git Bash on Windows. |
| PowerShell variants alongside bash | Native on Windows, but two implementations of every pattern to keep in sync. Divergence risk in the security guard. |
| Node scripts (cross-platform) | Node is already required for `src/web`, but it's a rewrite, and it's slower to start than bash for every edit. |

## Consequences

- **Windows prerequisites:** Git for Windows (Git Bash). `jq` is optional; `format.sh` has a grep fallback. Prettier comes from `npm ci` in `src/web`.
- **On a Windows machine without Git Bash,** the bash hooks can't run, and the fail-closed guard blocks Write/Edit until Git Bash is installed. That's intentional and visible.
- **The guard sees only Write/Edit/MultiEdit content,** not Bash-tool writes. It's a best-effort backstop, not the security architecture.
