# 0016 — Vitest for frontend unit tests

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/frontend.md` (Verification), `.claude/skills/new-component/SKILL.md`, CLAUDE.md (Usage)

## Context

The Angular workspace (`src/web`) has no test target yet, so `npm test` (`ng test`) fails. `tsconfig.site.spec.json` already declares `vitest/globals` types, while `.vscode/launch.json` still carries a Karma-style "ng test" debug entry from the original scaffold. Every behaviour change needs tests (CLAUDE.md), so the runner has to be chosen before frontend work starts.

## Decision

- **Frontend unit tests use Vitest,** run through `npm test` from `src/web`.
- The test target is added per project (`site` first, then `admin` and `ui`), with spec tsconfigs at the workspace root (`tsconfig.<project>.spec.json`, see `frontend.md`, Workspace config).
- Verify the Angular CLI's current unit-test builder configuration for Vitest against angular.dev when the target is added.
- Until it exists, agents run `npm run build` and say tests aren't available. They never claim tests passed.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Vitest (chosen)** | Fast, ESM-native, already assumed by the spec tsconfig. The Angular CLI integration is newer than Karma's, so builder options must be verified. |
| Karma + Jasmine | The long-standing Angular default and needs a real browser, but Karma is deprecated upstream. |
| Jest | Popular, but needs a separate preset and is less aligned with the CLI's build pipeline. |

## Consequences

- **Add `vitest` and the test target in the first prompt that adds frontend tests.** Fix or remove the stale Karma debug entry in `src/web/.vscode/launch.json` at the same time.
- **Browser-level and accessibility checks stay with Playwright** (`playwright-cli` skill). Vitest covers units and components.
