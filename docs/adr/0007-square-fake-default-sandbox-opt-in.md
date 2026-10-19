# 0007 — Square fake by default, sandbox opt-in

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/square.md`, `.claude/rules/aspire.md`, `.claude/rules/external.md`

## Context

Developers and CI must run the full system offline, with no Square account and zero prompts. Square behaviour (version conflicts, timeouts after send, duplicate and out-of-order webhooks) must be testable deterministically. Real sandbox runs are still needed to prove the integration end to end. Run mode must never reach Square production.

## Decision

- **`Square:Mode` is AppHost configuration: `fake` (default) or `sandbox`.** It decides which resources exist.
- **`fake`:** `Arts6311.SquareFake` is a local stand-in for the Square endpoints the gateways use, plus a signed-webhook trigger. No Square credentials are created or needed.
- **`sandbox`:** developer-supplied `square-access-token` and `square-webhook-signature-key` (user-secrets), plus a dev tunnel so Square's sandbox can deliver signed webhooks to the gateway.
- **Production credentials exist only in the prod Key Vault.** No run-mode path can reach production.
- **One contract test suite** runs against the fake (always) and the sandbox (opt-in through an environment flag).

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Fake by default + sandbox opt-in (chosen)** | Offline, deterministic, free. The fake must be kept faithful, which the shared contract suite checks against the sandbox. |
| Sandbox always | Real behaviour, but every developer needs a Square account, a tunnel and network access. Flaky CI; failure modes can't be injected. |
| Mock the SDK in unit tests only | Cheap, but no end-to-end run and no webhook path. Drift from real Square goes unnoticed. |

## Consequences

- `Arts6311.SquareFake` and the dev tunnel exist only in run mode. They're excluded from publish (ADR 0008).
- Anything touching Square must pass against the fake (CLAUDE.md). Sandbox runs back up claims about real behaviour.
- The runbook `docs/runbooks/square-sandbox.md` (prompt 5.20) documents the sandbox setup.
