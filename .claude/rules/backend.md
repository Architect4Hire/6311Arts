---
paths:
  - src/Arts6311.Api/**
  - src/Arts6311.Domain/**
  - src/Arts6311.Worker/**
  - src/Arts6311.MigrationService/**
---
# Backend Architecture

Every HTTP feature and every worker handler follows one seam:

```text
Controller → Facade → Business → DataLayer → Repository | Gateway
```

## Module ownership

The seam lives inside a module. `Arts6311.Domain` is organized by bounded context, not by layer. It has one assembly, one `DbContext` (`ArtsDbContext`) and one migration history.

```text
Modules/<Context>/
├── Facade/ Business/ Data/{Entities,Configurations,Repositories}/ Gateways/   # Gateways only where the module calls out;
│                                                                    # the DataLayer sits directly in Data/
├── Managers/            # this module's ViewModels, ServiceModels, domain models, validators, policies, outbox handlers
└── <Context>ServiceCollectionExtensions.cs

Managers/                # shared kernel: Persistence, Outbox, Idempotency, Caching, Time, Results, Paging, Audit,
                         # Publishing (content lifecycle, slugs), Concurrency (row-version ETags)
```

**Modules:**

- `Artists`
- `Exhibitions`
- `Classes`
- `Shop`
- `Memberships`
- `Pages`
- `Media`
- `Forms` (submissions, newsletter, contact)
- `Commerce` (cart, checkout, orders)
- `Members` (member accounts ↔ Square customers and subscriptions)
- `SquareSync` (catalog links, webhook inbox, reconciliation)
- `Redirects`
- `Import` (WordPress import staging and runs)
- `Identity` (internal session and credential operations)
- `Operations` (outbox replay/discard and other ops tooling; today only the public health-info echo)

**Module rules:**

- A module's ViewModels, ServiceModels, domain models and validators live in its own `Managers/` area. A `Models/` or `Validation/` tree at the domain root is a defect.
- Cross-module traffic is **facade to facade**. Only three things cross a module boundary:
  - the facade interface
  - the ServiceModels it returns
  - entity types, only where a foreign key already crosses
- The shared kernel never depends on a module. `ArtsDbContext` is the one documented exception.
- `ModuleBoundaryTests` enforces all of this.

## Layer ownership

**Controller.** Lives only in `Arts6311.Api/Controllers`. It:

- binds a ViewModel
- calls one facade operation
- maps the explicit result to HTTP

It holds no EF, cache, provider or domain logic, and it injects facades only.

**Facade.** The public application boundary, used by controllers *and* worker outbox handlers. It:

- runs FluentValidation
- coordinates policy checks
- reads and writes the cache
- wraps idempotent commands
- returns ServiceModels or result types

It never touches `DbContext`, repositories or gateways.

**Business.** It owns:

- domain invariants
- resource-level authorization
- deterministic calculations
- translation between ViewModel → domain → ServiceModel

It calls the DataLayer only, and knows nothing of HTTP, EF tracking, Square SDK types or cache transport.

**DataLayer.** It:

- composes whole persistence operations
- owns the transaction boundary
- stages outbox messages with `IOutboxWriter.Enqueue` *before* the single `SaveChangesAsync` that commits them (see `outbox.md`)
- chooses repository and gateway calls

It makes no product decisions.

**Repository and Gateway.**

- Repositories are EF Core queries and persistence only.
- Gateways are external calls only. `ISquare*Gateway` interfaces are implemented in `Arts6311.Square`, the only assembly that may reference the Square SDK.
- Provider types are mapped at the gateway boundary.

## Where an ambiguous rule goes

If the extra call is a **rule**, it belongs in Business. Example: "a class session can't start before its offering is published".

If it's **bookkeeping**, it belongs in the DataLayer. Example: "writing a CMS change also stages an outbox message".

Ask what a reviewer would call it.

## Contracts

- There's an interface between every pair of layers, injected through constructors.
- `CancellationToken` flows through every async call. Never use `.Result` or `.Wait()`.
- Failures are `ProblemDetails` with a stable error code and the correlation id.
- Use purpose-built repository methods. No generic repository.
- Money is `long` cents. Times are UTC `DateTimeOffset`. Class sessions also store the display time zone id (`America/Chicago`).

## Validation and transactions

- **Shape validation** happens at the edge (FluentValidation, in the facade).
- **Invariants** are checked in Business.
- **External constraints** are checked by gateways and translated into results.
- **Multi-write operations** use one `SaveChangesAsync` on one `ArtsDbContext`. That's already one transaction.
  - If an explicit transaction is unavoidable, run it through the execution strategy as a callback. The unit may run more than once, so it must be safe to repeat.
  - The one sanctioned case today is `IIdempotentCommandExecutor` (ADR 0020). It saves the claim, the wrapped operation's single save and the result inside one such transaction. A DataLayer wrapped by it still makes exactly one save.
- **A side effect that must follow a commit** (a Square push, an email, a rendition, a cache revalidation) is an outbox message. Never call it inline after `SaveChangesAsync`.

## Testing (per layer, mock the layer below)

| Layer | How it's tested |
|---|---|
| Repository | Integration tests against the shared SQL Server test container |
| DataLayer | Mocked repositories. Covers call order and that the outbox is staged before save. Plus **one real-database test** proving a failed save leaves neither the row nor the outbox message. |
| Business | Mocked DataLayer. Covers rules and mapping. |
| Facade | Mocked Business with a real validator. Covers cache hit, cache miss and validation failure. |
| Endpoint | `WebApplicationFactory` with the gateway's internal token. Covers the happy path, a validation failure and an authz failure. |

## Defects

The following are defects:

- `DbContext` in a controller or facade
- a repository called from Business
- Square SDK types outside `Arts6311.Square`
- `Facade/`, `Business/` or `Data/` folders in the API project
- blocking on async I/O
- calling an external system between "save" and "return" instead of through the outbox
- a Square-owned field written by a CMS path (see `square.md`)
