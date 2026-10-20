# Architecture Decision Records

One file per architectural decision, numbered in order: `NNNN-short-title.md`. CLAUDE.md requires an ADR for
every architectural decision. Rules in `.claude/rules/` cite the ADR they back.

## Status values

| Status | Meaning |
|---|---|
| `DECIDE` | Open. Options and costs are listed; nobody has chosen yet. Don't build on it. |
| `Accepted` | In force. |
| `Superseded by NNNN` | Replaced; kept for history. |

Never rewrite an accepted ADR's decision. Write a new ADR that supersedes it.

## Index

0011–0013 are reserved for the Square prompts that decide them (see `docs/architecture/architecture.md` §13).

| # | Title | Status |
|---|---|---|
| [0001](0001-single-origin-yarp-gateway-bff.md) | Single-origin YARP gateway as BFF | Accepted |
| [0002](0002-gateway-session-es256-internal-token.md) | Gateway session + ES256 internal token (no OIDC) | Accepted (creatorpantry B-02/B-13); amended by 0018 |
| [0003](0003-sql-transactional-outbox-no-broker.md) | SQL transactional outbox, no broker | Accepted |
| [0004](0004-module-first-domain-and-seam.md) | Module-first Domain + mandatory seam | Accepted |
| [0005](0005-square-field-ownership.md) | Square field ownership | Accepted |
| [0006](0006-square-ids-in-squarecataloglink.md) | Square ids in `SquareCatalogLink`, not entities | Accepted |
| [0007](0007-square-fake-default-sandbox-opt-in.md) | Square fake by default, sandbox opt-in | Accepted |
| [0008](0008-run-offline-publish-azure-container-apps.md) | Run mode offline / publish to Azure Container Apps | Accepted |
| [0009](0009-redis-for-session-tickets.md) | Redis for session tickets | Accepted; Azure hosting **DECIDE** (6.1) |
| [0010](0010-csoon-template-license.md) | CSoon template license | **DECIDE** before launch |
| 0011 | Square archive policy (delete vs mark unavailable) | Reserved, **DECIDE** in 5.5 |
| 0012 | Square subscription limits | Reserved, record in 5.10 |
| 0013 | Member class discount mechanism | Reserved, **DECIDE** in 5.19 |
| [0014](0014-static-web-apps-review-copy.md) | Temporary Static Web Apps review copy | Accepted, temporary (remove at 6.2) |
| [0015](0015-claude-code-hooks-bash.md) | Claude Code hooks are bash (Git Bash on Windows) | Accepted |
| [0016](0016-vitest-for-frontend-unit-tests.md) | Vitest for frontend unit tests | Accepted |
| [0017](0017-audit-log-database-immutability.md) | Database-level immutability for the audit log | **DECIDE** before production |
| [0018](0018-no-two-factor-authentication.md) | No two-factor authentication | Accepted |
| [0019](0019-correlation-id-is-the-w3c-trace-id.md) | The correlation id is the W3C trace id | Accepted |
| [0020](0020-idempotency-scoped-to-a-server-owned-subject.md) | Idempotency keys are scoped to a server-owned subject | Accepted |
| [0021](0021-api-host-conventions-and-internal-token-verification.md) | API host conventions + internal-token verification | Accepted |
| [0022](0022-gateway-edge-policy-and-internal-token-transform.md) | Gateway edge policy + internal-token transform | Accepted |
| [0023](0023-identity-module-and-email-through-the-outbox.md) | Identity module + email through the outbox | Accepted |
| [0024](0024-bff-session-tickets-and-edge-antiforgery.md) | BFF session tickets + antiforgery at the edge | Accepted |
| [0025](0025-ui-library-tokens-and-primitives.md) | `@6311/ui`: tokens, global styles and shared primitives | Accepted |
| [0026](0026-https-gateway-in-run-mode-and-the-platform-checkpoint.md) | HTTPS gateway in run mode, hermetic E2E checkpoint, CI | Accepted; forwarded-header trust owed by 6.1 |
| [0027](0027-admin-shell-and-staff-sign-in-page.md) | CMS shell + staff sign-in page on the site | Accepted; self-hosted fonts DECIDE before launch; amended by 0030 |
| [0028](0028-password-reset-and-email-confirmation-routes.md) | Password reset + email confirmation routes; the reset-request timing oracle | Accepted; amended by 0029 |
| [0029](0029-emailed-link-pages-and-confirmation-resend.md) | Emailed-link pages on the site; asking for a new confirmation link | Accepted |
| [0030](0030-media-library-compressed-masters-and-renditions.md) | Media library: SkiaSharp, compressed masters, metadata allow-list, content-addressed renditions | Accepted; publishing an image is DECIDE (owned by 2.8, see 0031) |
| [0031](0031-artists-and-the-shared-content-lifecycle.md) | Artists + the shared content lifecycle: status commands, slug lock, row-version tags, deferred effects | Accepted |

## Template

```markdown
# NNNN — Title

- **Status:** DECIDE | Accepted | Superseded by NNNN
- **Date:** YYYY-MM-DD
- **Rules affected:** .claude/rules/<file>.md
- **Adopts:** creatorpantry B-NN (only when a creatorpantry decision is adopted)

## Context
What forces the decision.

## Decision
What we do. (For DECIDE: leave empty and fill in Options.)

## Options considered
Each option with its cost and trade-offs.

## Consequences
What gets easier, what gets harder, what we now must not do.
```
