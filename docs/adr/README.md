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
| [0005](0005-square-field-ownership.md) | Square field ownership | Accepted; price and stock of artwork and classes superseded by 0032 |
| [0006](0006-square-ids-in-squarecataloglink.md) | Square ids in `SquareCatalogLink`, not entities | Accepted; created-in-Square rule superseded by 0032 |
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
| [0030](0030-media-library-compressed-masters-and-renditions.md) | Media library: SkiaSharp, compressed masters, metadata allow-list, content-addressed renditions | Accepted; publishing an image is DECIDE (owned by 3.1, see 0036) |
| [0031](0031-artists-and-the-shared-content-lifecycle.md) | Artists + the shared content lifecycle: status commands, slug lock, row-version tags, deferred effects | Accepted |
| [0032](0032-website-items-managed-from-the-cms.md) | Website items managed from the CMS (SKU, price and quantity commands); merch stays in Square POS | Accepted |
| [0033](0033-exhibitions-and-cross-module-artist-credits.md) | Exhibitions: artist credits facade to facade, calendar dates and UTC reception times, the content seed in MigrationService | Accepted |
| [0034](0034-html-sanitizer-and-pages.md) | The shared HTML sanitizer (HtmlSanitizer 9.2, allow-list, library-only images) and Pages; Tiptap 3 in the admin; placeholder pages everywhere | Accepted |
| [0035](0035-redirects-and-slug-changes.md) | Redirects: one hop, slug changes through the outbox (paths in the payload), a 5-minute site cache until 2.8, a SlugChange source | Accepted; the cache is now cleared by 0036 |
| [0036](0036-revalidation-scheduled-publishing-and-previews.md) | Revalidation by cache tag through a shared Redis L2 (≤1 minute stale — wrong, see 0049), the row-version-claimed scheduled publisher, Data Protection preview links, the Preview module | Accepted; media visibility still DECIDE, now owned by 3.1; cross-process reach amended by 0049 |
| [0037](0037-classes-and-the-shared-catalog-sku-registry.md) | Classes: the shared catalog SKU registry, session-level Square projections, sanitized class bodies | Accepted (amended by 0044: the run, not the session, holds the SKU and projections; by 0045: the body is "More details" on the slim allow-list) |
| [0038](0038-shop-items-artwork-categories-and-one-of-a-kind.md) | Shop items: artwork only, a category allow-list, an ordered gallery, the one-of-a-kind freeze | Accepted |
| [0039](0039-csp-hashes-for-the-sites-inline-scripts.md) | The site's three inline scripts are allowed by `sha256-` hash, never by nonce (cached HTML) or `'unsafe-inline'`; WordPress origins are development-only | Accepted |
| [0040](0040-membership-plans-without-a-page-of-their-own.md) | Membership plans have no page of their own (slug as a key, `/memberships` revalidated, no rename redirect), ordered perk rows, a CMS starting cadence, and `featured` as a plain flag | Accepted |
| [0041](0041-telemetry-does-not-export-log-scopes.md) | Log scopes are not exported (ASP.NET Core's hosting scope carries `RequestPath`, and a preview token is a path segment); YARP's forwarder is quietened to `Warning` | Accepted |
| [0042](0042-site-read-api-and-its-cache.md) | The site read API: published-only `/site/*` reads, cache entries tagged by every type they show, the site's own read models (also the preview's), a facade-only `Site` module, and image publishing decided as ADR 0030 option 1 (built in 3.1b) | Accepted (images: 3.1b; amended by 0046: the shop list carries `totalCount`) |
| [0043](0043-instructors-as-their-own-content-type.md) | Instructors are their own content type (not an artist flag): website and Instagram in one stored shape, an optional `Restrict` link to an artist, every edit revalidated, site reads cached under `instructor` and `artist` | Accepted |
| [0044](0044-class-runs-with-their-own-instructor-and-seats.md) | Classes are offering → runs → sessions: the run is the Square variation (SKU `class-run`, capacity, starting price, projections) and names an instructor by id; sessions are times only; site reads list upcoming runs and filter by instructor. Amends 0037 | Accepted |
| [0045](0045-one-slim-rich-text-allow-list.md) | One slim rich-text allow-list everywhere: `p`, one heading level (`h2`), bold, italic, lists, `br`; no links, pictures or quotes at this time; the class body is labelled "More details". Amends 0034, 0037 | Accepted |
| [0046](0046-the-site-reads-through-siteapi.md) | The site reads `/api/v1/site/*` through `SiteApi`: relative addresses on both sides, re-targeted to the gateway below the transfer cache (`GatewayBackend`); a failed read is a 503 `no-store`; sample data only behind `SITE_MOCK_FALLBACK`; the shop list carries `totalCount`; unpriced is not for sale; allowed hosts from `NG_ALLOWED_HOSTS`. Amends 0042 | Accepted |
| [0047](0047-forms-submissions-newsletter-and-contact.md) | Forms: artist submissions (answers as JSON, up to six pictures as hidden `Submission` media at 10 MiB), contact messages and the newsletter list; every public success a bodiless 202; fixed-word visitor confirmations; staff inbox per environment (`forms-staff-email`); Admin-only audited CSV export | Accepted (retention: DECIDE) |
| [0048](0048-site-routing-sitemap-forms-and-brand-images.md) | The site server routes and maps from the API: CMS redirects first, then the spike's map, `/product/{slug}` (class or shop) and `/artist/{slug}`; routing cached 60 s (5 s after a failure); the sitemap is a 503 with `Retry-After` when the API is down; the forms post to `/api/v1/forms/*`; the logo and social image are Media assets at fixed ids, seeded by MigrationService everywhere | Accepted |
| [0049](0049-cache-invalidation-bus.md) | A cache invalidation bus over Redis pub/sub: HybridCache reads a tag's invalidation time from L2 once per process, so the Worker's revalidations never reached the API (up to the 5-minute lifetime, measured in 3.4); every tag removed is published and applied by each other process. Amends 0036 | Accepted |
| [0050](0050-component-markup-and-styles-in-sibling-files.md) | A component is up to three sibling files: `<name>.ts` with `templateUrl`/`styleUrl`, `<name>.html`, `<name>.scss` (omitted when there are no styles); `template:`/`styles:` only in a `*.spec.ts` host component; extracted files take the `.ts` stem, or the kebab-cased class name when one file declares several components | Accepted |
| [0051](0051-one-component-per-folder.md) | One `@Component` per file and one folder per component, named after it, holding its `.ts`/`.html`/`.scss`/single-component `.spec.ts`; `shared/` grouped by kind (`layout`, `cards`, `forms`, `media`, `pickers`); no barrels re-exporting component groups, so a lazy route carries only its own code (a small win under SSR, where the motive is findability rather than speed). Amends 0050 | Accepted |

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
