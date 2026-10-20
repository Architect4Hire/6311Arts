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
| Public site reads | `GET /api/v1/site/classes`, `/site/classes/{slug}`, `/site/artists`, `/site/exhibitions`, `/site/shop`, `/site/memberships`, `/site/pages/{slug}`, `/site/redirects` | `[AllowAnonymous]` |
| CMS | `GET/POST /api/v1/cms/classes`, `PATCH /cms/classes/{id}`, `POST /cms/classes/{id}/publish` | `CmsEditor` (deletes `CmsAdmin`) |
| Commerce | `POST /api/v1/cart/items`, `POST /checkout/quote`, `POST /checkout/pay` | anonymous cart; pay is idempotent |
| Members | `POST /api/v1/members/signup`, `GET /members/me/subscription` | `Member` |
| Forms | `POST /api/v1/forms/submissions`, `/forms/newsletter`, `/forms/contact` | anonymous, rate limited |
| Operations | `GET /api/v1/cms/sync/status`, `POST /cms/sync/outbox/{id}/replay` | `CmsAdmin`, audited |
| Development-only scaffolding | none today (the 1.5 ping was removed in 1.12); mark any with `[DevelopmentOnly]` | 404 outside Development, before authorization runs |
| Internal | `/api/v1/internal/sessions/**`, `/api/v1/internal/identity/**` | gateway service token only |
| Webhooks | `POST /webhooks/square` | signature only |

## Shapes

- **Models.** Input is ViewModels and output is ServiceModels. Never return EF entities, Square DTOs or anonymous objects.
- **Times and money.** ISO 8601 UTC timestamps, written with an explicit `+00:00` offset (not `Z`); `format: date-time` can't tell the two apart, so the spelling is asserted in tests. Class sessions also return `displayTimeZone`. Money is integer cents with `currency`.
- **Paging.** Collections that change often use a cursor (`?cursor=`). Small fixed lists may return everything.
- **Errors.** `ProblemDetails` with a stable `code` (for example `cms.slug_taken`, `checkout.item_sold_out`, `square.unavailable`) and `correlationId`. The status follows the code, mapped in one place (`Arts6311.Api/Conventions/ApiProblemDetails.cs`, ADR 0021):

  | Code | Status |
  |---|---|
  | `validation.failed`, `paging.cursor.invalid`, `idempotency.key_required`, `idempotency.key_invalid` | 400 |
  | `auth.unauthenticated` | 401 |
  | `access.forbidden` | 403 |
  | `resource.not_found`, `request.route_not_found` | 404 |
  | `request.method_not_allowed` | 405 |
  | `request.payload_too_large` | 413 |
  | `request.unsupported_media_type` | 415 |
  | `resource.conflict`, `concurrency.conflict` | 409 |
  | `idempotency.key_reused` | 422 |
  | `server.error` (the whole 5xx family) | 500 |
  | `request.rejected` (the edge's remainder), anything unmapped | 400 |

  `square.unavailable` → 503 arrives with 5.2; a 429 code plus `Retry-After` and a missing-`If-Match` code arrive with the routes that need them. `ErrorCodeMapTests` fails if a code in `ErrorCodes` has no status here.

  A new code with any other status is added to that map deliberately. `code`, `correlationId` and `errors` are `ProblemDetails` extensions, which a generator can't infer, so the `ProblemDetails` component is hand-written to declare them. `correlationId` is the W3C trace id as 32 lower-case hex digits.
- **Concurrency.** Editable CMS records use a row version (`ETag` / `If-Match`). A stale write gets a 409 with code `concurrency.conflict`.
- **Idempotency.** Retryable commands require an `Idempotency-Key` header: checkout pay, membership signup, seat adjustment. A replay returns the original result and carries `Idempotent-Replayed: true`. A different body with the same key gets a 422 (`idempotency.key_reused`). Several `Idempotency-Key` values on one request count as none.
- **Long work.** Imports and full reconciliation return `202` with an operation resource.

## Compatibility

`src/Arts6311.Tests/Api/Snapshots/openapi-v1.json` is part of the contract. `OpenApiContractTests` fails on any shape change until the snapshot is regenerated (`UPDATE_OPENAPI_SNAPSHOT=1`) in the same change, and that diff is reviewed.

- **Breaking changes** need a new version or migration path. These are breaking: removing or renaming fields, changing nullability, narrowing accepted values, or changing ordering.
- **Generated TypeScript models must match.** The Angular models in both apps are checked against the snapshot by `api-contract-checker`.
