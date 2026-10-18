---
name: add-content-entity
description: Add a 6311arts CMS content type (or extend one) with EF configuration, publishing lifecycle, slugs, row version, audit, revalidation, migration and tests.
---

# Add a CMS Content Entity

Read `.claude/rules/content.md`. Then use `add-endpoint` for each route that exposes it.

1. **Place it in its module.** Entities go in `Modules/<Context>/Data/Entities/` and configurations in `Data/Configurations/`.
2. **Add the lifecycle fields:** `Status`, `PublishedAt`, `ScheduledFor`, `CreatedAt`, `UpdatedAt`, `UpdatedBy`, `RowVersion` (concurrency token). Add a `LegacyWordPressId` (nullable, unique where present) if the importer fills it.
3. **Slugs** are unique per type, and immutable once published. A change after publish creates a `Redirect` through the Redirects facade.
4. **Square-synced?** Then:
   - Add read-only projection columns for the Square-owned fields (`PriceCents`, `StockOnHand` / `SeatsRemaining`, `SoldOut`).
   - Write-side ViewModels and mappers must not contain them.
   - Square ids go on `SquareCatalogLink`, not here (`square.md`).
5. **Status commands** (`publish`, `unpublish`, `schedule`, `archive`) are separate operations. Each one, in its DataLayer, stages:
   - `site.revalidate-requested`
   - for synced types, the `square.*` message

   Use `add-outbox-message`.
6. **Body HTML** is sanitized on write with the shared allow-list sanitizer.
7. **Migration.** Generate it (`dotnet ef migrations add <Name> --project src/Arts6311.Domain --startup-project src/Arts6311.Api`). Review it, don't hand-edit it, and restart `aspire run` so MigrationService applies it.
8. **Seed.** Add development seed data in MigrationService, drawn from the spike's `data/*` mocks.

## Tests

- Slug uniqueness, and immutability after publish
- Every status transition, plus illegal transitions rejected
- A stale `RowVersion` gets a 409
- Publish stages the right outbox messages atomically (real database)
- Sanitizer strips scripts and inline handlers
- Synced type: Square-owned fields are absent from write models

## Checklist before done

- [ ] Entity, configuration, lifecycle, row version and slug rules in place
- [ ] Square-owned fields read-only projections only; no Square ids on the entity
- [ ] Status commands stage revalidation (+ Square) messages in the same save
- [ ] Migration reviewed and applied via MigrationService; seed data updated
- [ ] Tests pass; `@architecture-reviewer` run
