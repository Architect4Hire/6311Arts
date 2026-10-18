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

## Shape (shared kernel, `Managers/Outbox/`)

| Type | Role |
|---|---|
| `OutboxMessage` | `Id`, `Type` (routing key), `PayloadJson`, `CorrelationId`, `CreatedAt`, `AvailableAt`, `Attempts`, `Status`, `LeasedBy`, `LeaseExpiresAt`, `CompletedAt`, `LastError` |
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
  - `TryParse()` that returns `null` for anything malformed (the handler treats `null` as poison)

  Never put content, prices, tokens, card data or email bodies in a payload.
- **`CorrelationId`** carries the request's correlation id, so one trace runs from CMS save → message → Square call.
- **Message types are kebab-case `area.event`:**
  - `square.catalog-push-requested`
  - `square.catalog-delete-requested`
  - `square.inventory-adjust-requested`
  - `square.webhook-received`
  - `media.rendition-requested`
  - `email.send-requested`
  - `site.revalidate-requested`
  - `members.status-changed`

## Dispatching

- **Claims use concurrency tokens.** `Status` and `LeasedBy` are concurrency tokens, so each claim is its own guarded `UPDATE`. A `DbUpdateConcurrencyException` means another worker won: detach and move on. Running two Worker replicas is safe.
- **Expired leases are reclaimed.** A row whose lease expired (the worker died mid-handler) is claimed again. That's the crash-recovery path.
- **Each outcome is saved per message.** One message's exception never loses another's recorded outcome.
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
- **Handlers live in their module's `Managers/`** and are registered in that module's `ServiceCollectionExtensions` with `AddKeyedScoped<IOutboxMessageHandler, THandler>(TEvent.MessageType)`.

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
- A malformed payload poisons the message without calling a gateway.
- The last attempt records the visible terminal state.
- Two dispatchers racing on the same row deliver it once (dispatcher tests in the shared kernel).
