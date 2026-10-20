---
paths:
  - src/Arts6311.Domain/**
  - src/Arts6311.Worker/**
  - src/Arts6311.Api/Controllers/Webhooks/**
---
# Transactional Outbox

The outbox is the **only** way work leaves a committed transaction. It covers:

- Square pushes
- processing inbound Square webhooks
- emails
- media renditions
- cache and SSR revalidation
- member-status notifications

There's no message broker. The outbox table in `ArtsDbContext` *is* the queue, and `Arts6311.Worker` drains it. The pattern is the creatorpantry shared kernel (`Managers/Outbox/`), adopted as-is.

**The one sanctioned non-outbox queue** is the gateway's in-memory hand-off behind `/bff/forgot-password` and
`/bff/resend-confirmation` (ADR 0028, 0029). It is not an exception to the rule above: it carries a request to *start* a transaction, not the effect of one, and exists only so the browser is answered before anything that knows whether the address is registered runs. The effects it leads to — the audit row and the email — are still staged in the API's DataLayer and delivered through the outbox. It is not a precedent: a side effect after a commit is an outbox message, whichever process is asking.

## Shape (shared kernel, `Managers/Outbox/`)

| Type | Role |
|---|---|
| `OutboxMessage` | `Id`, `Type` (routing key), `PayloadJson`, `CorrelationId`, `TraceParent`, `CreatedAt`, `AvailableAt`, `Attempts`, `Status`, `LeasedBy`, `LeaseExpiresAt`, `CompletedAt`, `LastError` |
| `OutboxMessageStatus` | `Pending = 0`, `Leased = 10`, `Completed = 20`, `Poisoned = 30` — ordered, not a bitmask |
| `OutboxPolicy` | `BatchSize`, `MaxAttempts`, `LeaseDuration`, `PollingInterval`, `BackoffFor(attempts)` (doubling, capped) |
| `IOutboxWriter.Enqueue(type, payloadJson, correlationId)` | **Stages** a row on the ambient `ArtsDbContext`. Does not save. |
| `IOutboxDispatcher.DispatchDueAsync` | Claims due rows, invokes the keyed handler, records each outcome |
| `IOutboxMessageHandler.HandleAsync(OutboxMessageEnvelope, ct)` | One handler per `Type`, registered with keyed DI |
| `OutboxDispatcherHostedService` (Worker) | `PeriodicTimer` loop, a fresh DI scope per pass |

## Writing

- **A DataLayer stages the message and then makes the one `SaveChangesAsync`.** The domain write and the message commit together, or neither does.
  - Never save twice.
  - Never stage after saving.
  - Never call `Enqueue` from Business, a facade or a controller.
- **Payloads are ids only.** Use a typed event record (`Managers/Outbox/Events/` when shared, or the module's `Managers/` when one module both writes and handles it) with:
  - a `MessageType` constant
  - `Serialize()`
  - `TryParse()` that returns `null` for anything malformed. The handler throws `OutboxPoisonException` for `null`, and the dispatcher poisons the message on that attempt instead of retrying it.

  Never put content, prices, tokens, card data or email bodies in a payload.

  **The one documented exception:** `redirects.slug-changed` carries the two public paths, plus the rename's time (ADR 0035). A rename's old address is an immutable fact that no reload could recover, and a path is a public URL, not content or anyone's data. Any other exception needs its own ADR.
- **`CorrelationId`** carries the request's correlation id, so one trace runs from CMS save → message → Square call.
- **Message types are kebab-case `area.event`:**
  - `square.catalog-push-requested`
  - `square.catalog-delete-requested`
  - `square.price-change-requested` (payload `{requestId}`; the amounts live on the `SquareChangeRequest` row, ADR 0032)
  - `square.inventory-change-requested` (payload `{requestId}`: set quantity or capacity adjustment)
  - `square.webhook-received`
  - `media.rendition-requested`
  - `media.purge-requested` (a deleted asset's blobs, ADR 0030)
  - `email.send-requested`
  - `site.revalidate-requested`
  - `redirects.slug-changed` (`{contentType, contentId, fromPath, toPath, occurredAt}`; written by content modules, handled by Redirects, ADR 0035)
  - `members.status-changed`

## Dispatching

- **Claims use concurrency tokens.** `Status` and `LeasedBy` are concurrency tokens, so each claim is its own guarded `UPDATE`. A `DbUpdateConcurrencyException` means another worker won: detach and move on. Running two Worker replicas is safe.
- **Expired leases are reclaimed.** A row whose lease expired (the worker died mid-handler) is claimed again. That's the crash-recovery path.
- **Each message is claimed just before its handler runs, and the attempt is counted in the claim.** Its lease runs from when its handler starts. A handler that keeps crashing the Worker still uses up attempts: once a message has been claimed `MaxAttempts` times with no outcome recorded, the next claim poisons it.
- **Each handler runs in its own DI scope** with its own `ArtsDbContext`. Changes a failed handler made are never committed with the dispatcher's outcome save.
- **Only shutdown escapes a pass.** An `OperationCanceledException` stops the pass only when the dispatcher's own token is cancelled. Any other one (an `HttpClient` timeout, for example) is an ordinary failure and is retried.
- **Each outcome is saved per message.** One message's exception never loses another's recorded outcome.
- **One trace, no polling noise** (ADR 0019).
  - `Enqueue` captures the current W3C `traceparent`, but only when its trace is the message's `CorrelationId`.
  - Each claimed message runs inside an `outbox.process {Type}` consumer span parented on that traceparent, or on the correlation id when there is none. The handler joins the request's trace, and its `ICorrelationContext.Current` is the message's `CorrelationId`.
  - The Worker runs each poll pass under an unrecorded `outbox.poll` activity, so the poll's own SQL (the due read and the claims) is never exported.
    - This relies on a **parent-based sampler**. The Worker sets `ParentBased(AlwaysOn)` explicitly. A non-parent-based sampler (for example `OTEL_TRACES_SAMPLER=always_on`) would bring the poll noise back.
  - Inside a dispatcher pass, `Activity.Current` is the poll except within a message's `outbox.process` span. New dispatcher code that enqueues or reads the correlation id must run inside that span. The poll activity doesn't flow out of the pass, so other Worker jobs are unaffected.
- **Failure handling:**
  - A failure sets `Pending` with `AvailableAt = now + BackoffFor(attempts)`.
  - Exhausted attempts set `Poisoned`, which is terminal until an Admin replays or discards it on the Sync screen (audited).
- **`LastError` is sanitized.** A handler rethrows a generic message naming only the exception *type*. Never store provider messages, hosts, keys or tokens.

## Handling

- **Delivery is at-least-once.** Every handler is idempotent:
  - Key Square writes on the outbox message `Id` (the Square idempotency key).
  - Key emails on (`Type`, subject id).
  - Make renditions content-addressed.
- **Never trust the payload's state.** A handler reloads current state through a **facade** in its own scope. "Push item X" means "push X as it stands now", so an old or replayed message can't send stale content.
- **Out-of-order delivery is normal.** Compare versions and hashes, and skip when Square already has what we'd send.
- **On the last attempt** (`envelope.Attempt >= OutboxPolicy.MaxAttempts`), a handler records a terminal state the admin can see (for example `SyncStatus = NeedsAttention`) instead of throwing forever.
  - **Exception:** a handler that calls no provider can only fail when the database does, and then a terminal-state write would fail too. It may skip this step, with a remark saying so. The Poisoned message is its visible state.
- **Handlers live in their module's `Managers/`** and are registered in that module's `ServiceCollectionExtensions` with `TryAddKeyedScoped<IOutboxMessageHandler, THandler>(TEvent.MessageType)`.

## Inbound webhooks use the same mechanism

The Square webhook endpoint:

1. Verifies the signature.
2. Writes a `SquareWebhookEvent` inbox row. It has a unique index on Square's `event_id`; a duplicate insert is a no-op that still returns 200.
3. Stages `square.webhook-received` with the inbox row id.
4. Saves once.

All Square API calls happen in the handler, never in the HTTP request.

## Tests (required for every new message type)

- Staging plus a failed save leaves neither the row nor the message (real database).
- The handler is idempotent: run it twice and get one side effect.
- A malformed payload poisons the message on its first attempt (`OutboxPoisonException`) without calling a gateway.
- The last attempt records the visible terminal state.
- Two dispatchers racing on the same row deliver it once (dispatcher tests in the shared kernel).
