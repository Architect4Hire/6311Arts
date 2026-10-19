---
name: add-outbox-message
description: Add a new transactional-outbox message type to 6311arts — the event record, the DataLayer staging, the keyed Worker handler, retries/poison behaviour and the required atomicity and idempotency tests.
---

# Add an Outbox Message

Read `.claude/rules/outbox.md` first. Use this whenever work must happen **after** a commit:

- a Square push or adjustment
- processing a webhook
- an email
- a media rendition
- cache or SSR revalidation
- a member notification

## 1. Name and shape the event

- **Message type:** kebab-case `area.event`, for example `square.catalog-push-requested`.
- **Event record:** a `sealed record` in the owning module's `Managers/` when one module both writes and handles it, or in `Managers/Outbox/Events/` if two modules must both name it (`outbox.md`). It has:
  - `public const string MessageType`
  - `Serialize()` using web JSON defaults
  - `static TryParse(string)` that returns `null` for malformed or empty-id payloads
- **Payload:** ids only (entity id, link id, inbox row id, initiating user id). Never content, prices, tokens, emails or card data.

## 2. Stage it in the DataLayer

```csharp
// inside the DataLayer method that already writes the entity
_repository.Update(item);
_outbox.Enqueue(ShopItemPushRequested.MessageType,
                new ShopItemPushRequested(item.Id, actorId).Serialize(),
                _correlation.Current);
await _repository.SaveChangesAsync(ct);   // one save: row + message commit together
```

- **The DataLayer is the only caller of `IOutboxWriter`.** Not Business, not a facade, not a controller.
- **Never save between the write and the `Enqueue`.** Never stage after the save.

## 3. Write the handler (Worker side, in the module's `Managers/`)

```csharp
// The dispatcher resolves each handler in a DI scope of its own per message (outbox.md), so the facade it
// injects is already the handler's own: no IServiceScopeFactory needed.
internal sealed class ShopItemPushRequestedHandler(IShopSyncFacade facade, ILogger<...> log)
    : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope m, CancellationToken ct)
    {
        var evt = ShopItemPushRequested.TryParse(m.PayloadJson)
                  ?? throw new OutboxPoisonException("Malformed shop-item push payload."); // poisoned now, not retried
        try
        {
            // reload CURRENT state through the facade; idempotency key = m.Id
            await facade.PushAsync(evt.ShopItemId, idempotencyKey: m.Id,
                                   lastAttempt: m.Attempt >= OutboxPolicy.MaxAttempts, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // never let a provider message (hosts, keys) reach OutboxMessage.LastError or the logs:
            // name only the exception type, and don't chain `ex` as the InnerException (outbox.md)
            throw new InvalidOperationException($"Shop item push failed ({ex.GetType().Name}).");
        }
    }
}
```

Handler rules:

- **Reload, don't trust.** Load current state through a facade. The payload only names what to look at.
- **Idempotent.** Key external writes on `m.Id`, and skip when the target already matches (version or hash).
- **Visible on the last attempt.** When `lastAttempt` is true, record a staff-visible terminal state (for example `SyncStatus = NeedsAttention`) instead of failing silently.
- **Log identifiers only.**

Register it in the module's `ServiceCollectionExtensions`:

```csharp
services.TryAddKeyedScoped<IOutboxMessageHandler, ShopItemPushRequestedHandler>(ShopItemPushRequested.MessageType);
```

## 4. Tests

- [ ] **Atomicity (real DB):** forcing the save to fail leaves neither the entity change nor the outbox row
- [ ] **Staging:** the DataLayer test asserts `Enqueue` is called before `SaveChangesAsync`, exactly once
- [ ] **Idempotency:** running the handler twice with the same envelope gives one external effect (assert on the Square fake / mail sink)
- [ ] **Poison:** a malformed payload throws `OutboxPoisonException` without calling any gateway, and the message is Poisoned on attempt 1
- [ ] **Stale message:** a message for an item changed since enqueue pushes the *current* state
- [ ] **Last attempt:** the terminal state is recorded and visible on the Sync screen
- [ ] **No secrets:** no secret or provider message ends up in `LastError` or the logs

## Checklist before done

- [ ] Event record has `MessageType`, `Serialize`, `TryParse`; payload is ids only
- [ ] Staged in the DataLayer before the single `SaveChangesAsync`
- [ ] Handler keyed-registered, reloads state via a facade, idempotent, sanitizes errors
- [ ] Last-attempt behaviour records a visible terminal state
- [ ] All tests above pass; `@outbox-reviewer` run and findings addressed
