---
name: add-endpoint
description: Build or extend a 6311arts HTTP endpoint through the complete Controller → Facade → Business → DataLayer → Repository | Gateway seam, with the outbox, idempotency and tests it needs.
---

# Add an Endpoint

Use this for every API route. There's no controller-only shortcut.

## 1. Classify the endpoint before editing

Answer these first:

- **Route family and policy** (`api-contract.md`): site (anonymous), cms (`CmsEditor` / `CmsAdmin`), commerce, members, forms, operations, or internal.
- **Operation type:** read, create, update, status command, delete or archive, idempotent command, or long-running (`202`).
- **Module** that owns it, and whether it calls another module (facade to facade only).
- **Field ownership:** does it touch a Square-synced type? If so, list which fields are CMS-owned and confirm no Square-owned field is writable (`square.md`).
- **Effects after commit:** does it need an outbox message (Square push, revalidation, email, rendition)? Use `add-outbox-message`.
- **Other mechanics:** concurrency (row version), idempotency (`Idempotency-Key`), caching (tags to invalidate), audit.
- **Errors:** what stable error codes does it introduce?

## 2. Define the contract first

1. **Request ViewModel** in the module's `Managers/ViewModels/`.
2. **FluentValidation validator**, for shape and format only. Data-dependent rules go in Business.
3. **Response ServiceModel** in the module's `Managers/ServiceModels/`.
4. **Explicit result and error types** for expected failures.
5. **OpenAPI snapshot update** and the matching Angular model, if a UI consumes it.

Never return EF entities, Square DTOs or anonymous objects.

## 3. Build bottom-up

Each layer is written against the one below, which already exists.

1. **Repository.** Narrow EF methods. No-tracking reads where safe. List reads project straight to the summary ServiceModel.
2. **Gateway** (if needed). Use the existing `ISquare*Gateway` or another provider interface. Never call providers from the request path when the call can follow the commit; that's an outbox handler's job.
3. **DataLayer.**
   - Compose repository and gateway calls into one operation.
   - Stage outbox messages with `IOutboxWriter.Enqueue` *then* call `SaveChangesAsync` once.
   - Own the transaction.
4. **Business.**
   - Translate ViewModel → domain.
   - Enforce invariants and resource-level authorization.
   - Map domain → ServiceModel.
   - Call the DataLayer only.
5. **Facade.**
   - Validate.
   - Check policy.
   - Read from and invalidate the cache (HybridCache tags).
   - Wrap idempotent commands in `IIdempotentCommandExecutor`.
   - Call Business once.
6. **Controller.**
   - Versioned route and policy.
   - Bind route and ViewModel; never bind ids the server should own.
   - Call the facade and map the result.
   - Return `201` with location / `202` / `409` / `ProblemDetails`.
   - Pass the `CancellationToken` through.
7. **DI.** Register in the module's `ServiceCollectionExtensions`. Validators are discovered by assembly scan already, so don't add a second scan line.

## 4. Tests (per layer, mock the layer below)

- **Validator:** valid input, plus boundary cases.
- **Repository:** integration test against the SQL Server test container.
- **DataLayer:** call order, the outbox staged before the save, and a short-circuit on failure. Plus **one real-database test**: a failing save leaves neither the entity nor the outbox row.
- **Business:** rules, authorization and mapping.
- **Facade:** cache hit, cache miss, validation failure, idempotent replay (if idempotent).
- **Endpoint** (`WebApplicationFactory` + internal token): happy path, validation failure, wrong role (403), conflict (409) where relevant.
- **Square-synced:** proof that the Square-owned fields can't be written.

## 5. Verify and report

Run the focused tests, then the full suite, then `@architecture-reviewer` and `@api-contract-checker` (plus `@outbox-reviewer` if a message was added). Report:

- the route and policy
- the seam files
- outbox messages added
- error codes
- verification run
- open decisions

## Checklist before done

- [ ] Route versioned, in the right family, with the right policy; anonymous only with an explicit `[AllowAnonymous]`
- [ ] ViewModel + validator + ServiceModel explicit; nothing else crosses the controller
- [ ] Every layer calls only the next layer, through interfaces; cross-module calls are facade to facade
- [ ] Post-commit effects are outbox messages staged before the single save — no inline provider call after save
- [ ] No Square-owned field writable from this route
- [ ] Concurrency, idempotency, cache invalidation and audit are deliberate, not accidental
- [ ] `CancellationToken` flows through all I/O
- [ ] Tests per layer pass, including the real-database atomicity test
- [ ] OpenAPI snapshot regenerated and diff reviewed; Angular models updated
