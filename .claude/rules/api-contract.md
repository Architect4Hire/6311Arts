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
| Operations (scaffolding) | `POST /api/v1/cms/ops/ping`: 1.5 only, removed in 1.12 | Development only (404 elsewhere); `CmsAdmin` from 1.7 |
| Internal | `/api/v1/internal/sessions/**` | gateway service token only |
| Webhooks | `POST /webhooks/square` | signature only |

## Shapes

- **Models.** Input is ViewModels and output is ServiceModels. Never return EF entities, Square DTOs or anonymous objects.
- **Times and money.** ISO 8601 UTC timestamps. Class sessions also return `displayTimeZone`. Money is integer cents with `currency`.
- **Paging.** Collections that change often use a cursor (`?cursor=`). Small fixed lists may return everything.
- **Errors.** `ProblemDetails` with a stable `code` (for example `cms.slug_taken`, `checkout.item_sold_out`, `square.unavailable`) and `correlationId`.
- **Concurrency.** Editable CMS records use a row version (`ETag` / `If-Match`). A stale write gets a 409 with code `concurrency.conflict`.
- **Idempotency.** Retryable commands require an `Idempotency-Key` header: checkout pay, membership signup, seat adjustment. A replay returns the original result. A different body with the same key gets a 422.
- **Long work.** Imports and full reconciliation return `202` with an operation resource.

## Compatibility

`src/Arts6311.Tests/Api/Snapshots/openapi-v1.json` is part of the contract. `OpenApiContractTests` fails on any shape change until the snapshot is regenerated (`UPDATE_OPENAPI_SNAPSHOT=1`) in the same change, and that diff is reviewed.

- **Breaking changes** need a new version or migration path. These are breaking: removing or renaming fields, changing nullability, narrowing accepted values, or changing ordering.
- **Generated TypeScript models must match.** The Angular models in both apps are checked against the snapshot by `api-contract-checker`.
