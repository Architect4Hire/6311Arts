---
paths:
  - src/Arts6311.Api/**
  - src/Arts6311.Tests/Api/**
  - src/web/projects/*/src/app/models/**
  - src/web/projects/*/src/app/services/**
---
# API Contract

All routes are versioned from the first endpoint: `/api/v1/...`.

## Route families

| Family | Example | Auth |
|---|---|---|
| Public site reads | `GET /api/v1/site/classes` (`?instructor=`), `/site/classes/{slug}`, `/site/instructors`, `/site/instructors/{slug}`, `/site/exhibitions`, `/site/exhibitions/{slug}`, `/site/artists`, `/site/artists/{slug}`, `/site/shop` (`?category=&artist=&sort=&cursor=&limit=`; a cursor page plus `totalCount`, ADR 0046), `/site/shop/{slug}`, `/site/memberships`, `/site/pages/{slug}`, `/site/sitemap`, `/site/redirects` | `[AllowAnonymous]`; Published records only, except that a credit names its artist whatever their status (`slug` is `null` unless they are published); the site's own `Site*ServiceModel`s (no status, bookkeeping, SKU or stock count); `Cache-Control: public, max-age=60`, a 404 `no-store` (ADR 0042) |
| Preview | `GET /api/v1/site/preview/{token}` | `[AllowAnonymous]`; the signed, expiring token is the credential (ADR 0036). `no-store`, `X-Robots-Tag: noindex`, read-only; anything unusable is 404 |
| CMS | `GET/POST /api/v1/cms/classes`, `PATCH /cms/classes/{id}`, `POST /cms/classes/{id}/publish`; the same shape for `/cms/artists`, `/cms/instructors`, `/cms/exhibitions`, `/cms/pages`, `/cms/shop` (artwork; `?category=` filters it; `POST /cms/shop/import/preview` and `/cms/shop/import` take a CSV, ADR 0053), `/cms/memberships` (tiers, in display order) and `/cms/media` | `CmsEditor` (deletes `CmsAdmin`) |
| CMS status commands | `POST /api/v1/cms/{type}/{id}/publish`, `/unpublish`, `/schedule` (body `ContentScheduleViewModel`), `/unschedule`, `/archive` (`CmsAdmin`) | `CmsEditor`; every one carries `If-Match` |
| CMS delete and restore (ADR 0052) | `DELETE /api/v1/cms/{type}/{id}` → `204`, `POST /api/v1/cms/{type}/{id}/restore` → the record as a Draft, `GET /api/v1/cms/{type}/deleted` → a cursor page of `{type}DeletedServiceModel` (`id`, name or title, `slug`, the `status` it had, `deletedAt`, `etag`) | `CmsAdmin`, audited; delete and restore carry `If-Match` (missing → 428) |
| CMS previews | `POST /api/v1/cms/preview` → `{ token, path, expiresAt }` | `CmsEditor`; mints a link for one record, changes nothing |
| Commerce | `POST /api/v1/cart/items`, `POST /checkout/quote`, `POST /checkout/pay` | anonymous cart; pay is idempotent |
| Members | `POST /api/v1/members/signup`, `GET /members/me/subscription` | `Member` |
| Forms | `POST /api/v1/forms/submissions` (multipart: a `submission` JSON part, then up to six `images`), `/forms/newsletter`, `/forms/contact` | anonymous, `forms` rate limit, antiforgery; every success a bodiless `202`, a honeypot hit included (ADR 0047) |
| CMS forms | `GET/PATCH /api/v1/cms/forms/submissions[/{id}]`, `/cms/forms/messages[/{id}]`, `GET /cms/forms/newsletter`, `DELETE /cms/forms/newsletter/{id}`, `GET /cms/forms/newsletter/export` (CSV) | `CmsEditor`; the newsletter delete and export `CmsAdmin`, audited (ADR 0047) |
| Operations | `GET /api/v1/cms/sync/status`, `POST /cms/sync/outbox/{id}/replay` | `CmsAdmin`, audited |
| Development-only scaffolding | none today (the 1.5 ping was removed in 1.12); mark any with `[DevelopmentOnly]` | 404 outside Development, before authorization runs |
| Internal | `/api/v1/internal/sessions/**`, `/api/v1/internal/identity/**` | gateway service token only |
| Webhooks | `POST /webhooks/square` | signature only |
| Media delivery | `GET/HEAD /media/{id}/{rendition}`: unversioned file URLs, outside OpenAPI (ADR 0030) | `[AllowAnonymous]`; drafts are staff-only, enforced in Business |

## Shapes

- **Models.** Input is ViewModels and output is ServiceModels. Never return EF entities, Square DTOs or anonymous objects.
- **Times and money.** ISO 8601 UTC timestamps, written with an explicit `+00:00` offset (not `Z`); `format: date-time` can't tell the two apart, so the spelling is asserted in tests. Class runs also return `displayTimeZone`, the zone their sessions are shown in. Money is integer cents with `currency`.
- **Paging.** Collections that change often use a cursor (`?cursor=`). Small fixed lists may return everything.
- **Errors.** `ProblemDetails` with a stable `code` (for example `cms.slug_taken`, `checkout.item_sold_out`, `square.unavailable`) and `correlationId`. The status follows the code, mapped in one place (`Arts6311.Api/Conventions/ApiProblemDetails.cs`, ADR 0021):

  | Code | Status |
  |---|---|
  | `validation.failed`, `paging.cursor.invalid`, `idempotency.key_required`, `idempotency.key_invalid` | 400 |
  | `auth.unauthenticated` | 401 |
  | `auth.credentials_invalid`, `auth.locked_out`, `auth.email_unconfirmed`, `auth.password_rejected`, `auth.token_invalid` (internal routes only) | 400 |
  | `access.forbidden` | 403 |
  | `resource.not_found`, `request.route_not_found` | 404 |
  | `request.method_not_allowed` | 405 |
  | `request.payload_too_large` | 413 |
  | `request.unsupported_media_type` | 415 |
  | `resource.conflict`, `concurrency.conflict`, `media.duplicate`, `media.in_use`, `cms.slug_taken`, `cms.slug_locked` (no longer returned since 2.7), `cms.status_transition_invalid`, `cms.in_use`, `cms.sku_taken`, `redirects.from_taken`, `redirects.loop_or_chain` | 409 |
  | `concurrency.precondition_required` (no `If-Match`; a malformed one is `validation.failed`) | 428 |
  | `media.too_large` | 413 |
  | `media.type_unsupported`, `media.extension_mismatch` | 415 |
  | `media.dimensions_out_of_range`, `media.undecodable` | 400 |
  | `idempotency.key_reused` | 422 |
  | `server.error` (the whole 5xx family) | 500 |
  | `request.rejected` (the edge's remainder), anything unmapped | 400 |

  `square.unavailable` → 503 arrives with 5.2; a 429 code plus `Retry-After` arrives with the routes that need it. The missing-`If-Match` code arrived with media (2.3). `ErrorCodeMapTests` fails if a code in `ErrorCodes` or `IdentityErrorCodes` has no status here. The identity codes are reachable only on `/api/v1/internal/**`, and the gateway collapses the first three into one browser answer (ADR 0023) — but they are codes in the contract, so "one code, one status" covers them. The test asks `ApiProblemDetails.IsDeclared` rather than comparing against `UnmappedCodeStatus`, because a code declared as 400 and a code that fell back to 400 are otherwise indistinguishable, which is how these five went unnoticed.

  A new code with any other status is added to that map deliberately. `code`, `correlationId` and `errors` are `ProblemDetails` extensions, which a generator can't infer, so the `ProblemDetails` component is hand-written to declare them. `correlationId` is the W3C trace id as 32 lower-case hex digits.
- **Concurrency.** Editable CMS records use a row version (`ETag` / `If-Match`). A stale write gets a 409 with code `concurrency.conflict`.
- **Idempotency.** Retryable commands require an `Idempotency-Key` header: checkout pay, membership signup, seat adjustment, the shop CSV import. A replay returns the original result and carries `Idempotent-Replayed: true`. A different body with the same key gets a 422 (`idempotency.key_reused`). Several `Idempotency-Key` values on one request count as none.
- **Long work.** Imports and full reconciliation return `202` with an operation resource. The shop CSV import is the
  exception: it is bounded at 1,000 rows in a single save, so it answers synchronously (ADR 0053).

## Compatibility

`src/Arts6311.Tests/Api/Snapshots/openapi-v1.json` is part of the contract. `OpenApiContractTests` fails on any shape change until the snapshot is regenerated (`UPDATE_OPENAPI_SNAPSHOT=1`) in the same change, and that diff is reviewed.

- **Breaking changes** need a new version or migration path. These are breaking: removing or renaming fields, changing nullability, narrowing accepted values, or changing ordering.
- **Generated TypeScript models must match.** The Angular models in both apps are checked against the snapshot by `api-contract-checker`.
