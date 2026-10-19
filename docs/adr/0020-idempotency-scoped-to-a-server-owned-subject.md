# 0020 — Idempotency keys are scoped to a server-owned subject

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/api-contract.md` ("Idempotency"), `.claude/rules/backend.md` ("Facade … wraps idempotent commands"), `.claude/rules/aspire.md` (`idempotency-fingerprint-key`)
- **Adopts:** the creatorpantry shared-kernel idempotency (`Managers/Idempotency/`), with the deltas below. It has no B-number in creatorpantry's baseline.

## Context

Retryable commands (checkout pay, membership signup, seat adjustment) take an `Idempotency-Key`. A replay returns the original result, and the same key with a different body is refused (api-contract.md). creatorpantry's kernel does this:

- An HMAC-SHA256 fingerprint of the request is keyed by a server secret, so the stored hash can't be tested against guesses.
- The claim, the wrapped operation and the result write commit in one transaction.
- A unique scope index serializes concurrent first use.

creatorpantry scopes a key to `(UserId, WorkspaceId, Operation, Key)`. 6311arts has no workspaces, and **checkout pay is anonymous** (an anonymous cart), so there is not always a user id.

## Decision

- **The scope is `(Subject, Operation, Key)`, unique.** `Subject` is a server-owned handle, never request input:
  - `user:{id}` for an authenticated caller;
  - a handle the server issued for an anonymous command, for example `cart:{id}`.

  One caller's key can therefore never replay another caller's result.
- **Scope columns are ASCII and binary-collated.**
  - All three are validated as printable ASCII and stored as `varchar`. As `nvarchar` the unique index would be 1,710 bytes, past SQL Server's 1,700-byte limit for a nonclustered key.
  - All three use `Latin1_General_BIN2`. The database default is case-insensitive, so `Abc` and `abc` would otherwise share a scope.
- **A failed claim is decided by what the database holds.** If the scope's record now exists, the request lost the race and replays. Otherwise the claim retries briefly, then rethrows the original exception so the execution strategy can retry a transient one. No provider error numbers are involved.
- **Only the API receives the fingerprint key.** The AppHost passes `idempotency-fingerprint-key` to the API as `Idempotency__FingerprintKey`, validated at startup. The Worker registers only `IIdempotencyMaintenance` and never holds the key.
- **The Worker sweeps expired records hourly.** Each batch is one set-based `DELETE` of 500 rows; the sweep repeats until a batch comes back short. A record past `Retention` (24 h) is also deleted lazily when its scope is next used. Sweeps need no claim across replicas: deleting dead rows twice is harmless.
- **Opt-in only.** A facade wraps its single Business call in `IIdempotentCommandExecutor`. Nothing applies it automatically. The HTTP mapping is 1.7's job:
  - the `Idempotency-Key` header;
  - `Idempotent-Replayed` on replays;
  - `idempotency.key_reused` → 422.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Server-owned subject (chosen)** | Works for anonymous and authenticated commands alike. The facade must choose the subject correctly; it's documented on `IdempotentCommand`. |
| Keep `UserId`; require sign-in for idempotent commands | Simple, but forbids anonymous checkout, which the product requires. |
| Scope anonymous keys to the operation only | One visitor's key could replay another's result, a data leak. Rejected. |
| A single key-only scope | Same leak, for everyone. Rejected. |

## Sanctioned exceptions

Both were flagged by `@architecture-reviewer` on 1.6 and accepted here.

- **Several saves in one transaction (backend.md "one `SaveChangesAsync`").** The executor saves the claim, then the wrapped operation makes its own single save, then the executor saves the result. All three happen inside one explicit transaction, run through the execution strategy, so they commit together or not at all. This is the case backend.md allows ("if an explicit transaction is unavoidable"). The claim must come before the operation, so a single save isn't possible. Only the idempotency kernel has this exception.
- **The sweep calls a kernel port, not a module facade (add-background-job step 3).** `IIdempotencyMaintenance` is the shared kernel's own housekeeping boundary. No module owns the table, so there's no module facade to go through. The Worker resolves the port, never the DbContext or repository. Other jobs still go through module facades.

## Open decisions

- **DECIDE in 1.7:** which status code does `idempotency.key_required` map to, 400 or 428 Precondition Required? Also, an `ArgumentException` from the executor (a facade passing a bad subject or operation) is a server bug and must map to 500, never 400.
- **DECIDE in 5.14:** checkout pay must call Square inside the request, because the visitor waits for the payment result. A failed result isn't remembered, so if Square charges and the commit then fails, a retry runs the operation again. Square's own idempotency key for the payment must therefore be stable across retries, derived from the subject and the client's `Idempotency-Key`, and never random per attempt. The cart id must also be unguessable, because whoever holds the cart id and the key can replay the stored result.

## Consequences

- **Anonymous idempotent commands need a server-issued handle** (the cart id) before they can opt in. 5.13 and 5.14 provide one.
- **The wrapped operation may run more than once.** The execution strategy retries the whole transaction on a transient fault, and the failed attempt rolls back.
  - Its writes and outbox messages commit with the record, or not at all.
  - A repeated run is safe only for writes that roll back. A provider call doesn't roll back, so backend.md already sends after-commit effects through the outbox. The one inline call the product needs, checkout pay, is the 5.14 DECIDE above.
- **Rotating the fingerprint key invalidates live keys.** Every key used in the past 24 h then answers 422 (`key_reused`) instead of replaying. Rotate during a quiet window, or accept a day of 422s on retries.
- **Must not:**
  - build a `Subject` from request input;
  - stage anything on the request's DbContext before calling the executor (it clears the change tracker);
  - give the Worker the fingerprint key.
