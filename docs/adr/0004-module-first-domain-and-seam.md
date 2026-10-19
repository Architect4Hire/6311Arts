# 0004 — Module-first Domain + mandatory seam

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/backend.md`, `.claude/rules/api-contract.md`, `.claude/rules/outbox.md`
- **Adopts:** the creatorpantry seam and module-first Domain layout. It has no B-number in creatorpantry's baseline.

## Context

The platform covers 14 bounded contexts (Artists, Exhibitions, Classes, Shop, Memberships, Pages, Media, Forms, Commerce, Members, SquareSync, Redirects, Import, Identity), plus a shared kernel (Persistence, Outbox, Idempotency, Caching, Time, Results, Paging, Audit). Without a fixed shape, controllers grow data access, provider DTOs leak through the API, and modules reach into each other's tables.

## Decision

- **One seam for every HTTP feature and worker handler:** `Controller → Facade → Business → DataLayer → Repository | Gateway`. Each layer calls only the next one, through interfaces.
- **Controllers live only in `Arts6311.Api` and inject facades only.** Everything below lives in `Arts6311.Domain`.
- **`Arts6311.Domain` is organized by bounded context, not by layer:** `Modules/<Context>/{Facade,Business,Data,Gateways,Managers}`. It has one assembly, one `ArtsDbContext` and one migration history. The shared kernel lives in `Managers/` and never depends on a module (`ArtsDbContext` is the one documented exception).
- **Cross-module traffic is facade to facade.** Only facade interfaces, their ServiceModels, and entity types already joined by a foreign key cross a boundary.
- **ViewModels in, ServiceModels out.** EF entities and Square DTOs never cross the API boundary.
- `ModuleBoundaryTests` enforces this (planned, Phase 1).

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Module-first single Domain assembly + seam (chosen)** | Clear ownership; one DbContext keeps outbox staging atomic (ADR 0003). Needs boundary tests to stop drift inside one assembly. |
| Layer-first projects (`Arts6311.Data`, `Arts6311.Sync`, …; v1 plan) | Familiar, but features are spread across projects and module ownership is invisible. Doesn't match the seam or the boundary tests. |
| Assembly per module | Compiler-enforced boundaries, but many projects, cross-assembly EF configuration, and harder single-transaction outbox staging. |
| Microservices per context | Independent deploys, but distributed transactions, more Azure cost, and far more than a nonprofit site needs. |

## Consequences

- Every new route goes through the `add-endpoint` skill, and every module follows the folder shape.
- More types per feature (facade, business, data layer interfaces) in exchange for testability per layer (mock the layer below).
- A `Models/` or `Validation/` tree at the Domain root is a defect.
