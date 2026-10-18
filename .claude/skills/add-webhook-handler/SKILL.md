---
name: add-webhook-handler
description: Handle a new Square webhook event type in 6311arts — signature-verified intake into the SquareWebhookEvent inbox, outbox hand-off, and an idempotent Worker handler that fetches current state.
---

# Add a Square Webhook Handler

Read `square.md` (Reads) and `outbox.md` (Inbound webhooks) first.

## 1. Confirm the event on Square's docs

Record these in the plan, with the developer.squareup.com reference:

- the event type name
- the payload fields (only ids and timestamps are used)
- the OAuth/permission scope it needs
- whether ordering is guaranteed (assume it isn't)

## 2. Intake (already exists; extend only the type allow-list)

`POST /webhooks/square`:

1. Read the raw body.
2. Verify the HMAC-SHA256 over the configured public notification URL + body, in constant time. A failure gets a 401 and increments the `square.webhook.signature_failures` metric.
3. If the type isn't on the allow-list, return 200 and ignore it (counted).
4. Insert `SquareWebhookEvent` (`EventId` unique, `Type`, `ObjectIds`, `CreatedAt`, `ReceivedAt`). A duplicate is a no-op that returns 200.
5. Stage `square.webhook-received {inboxId}` and save once. No Square calls here.

## 3. Handler

The `square.webhook-received` handler dispatches by event type to a module facade method:

- **`catalog.version.updated`** → `SquareSync`: search the catalog since the cursor, apply Square-owned fields, advance the cursor after commit.
- **`inventory.count.updated`** → batch-retrieve counts for the named variations, update stock or seats.
- **`subscription.created` / `subscription.updated`** → `Members`: retrieve the subscription, set member status.
- **`invoice.payment_made` / `payment.updated` (FAILED)** → `Members`: flag the renewal failure and stage `email.send-requested`.

Every branch:

- **Never trusts payload state.** It fetches current state from Square.
- **Is idempotent.** It marks the inbox row `Processed` and short-circuits if it already is.
- **Tolerates out-of-order delivery** by comparing versions and timestamps against stored state.
- **Stages follow-up messages** (revalidation, email) in the same save that applies the change.

## 4. Tests (use the fake's signed-webhook trigger)

- [ ] Valid signature is processed; bad signature gets a 401 and nothing is stored
- [ ] Duplicate `event_id` gets one effect
- [ ] Out-of-order: an older event after a newer one leaves the newer state
- [ ] A payload with stale values still results in Square's current values
- [ ] An unknown type is ignored with a 200
- [ ] The handler crashing after applying but before marking is safe to redeliver

## Checklist before done

- [ ] Event documented with its source link
- [ ] Intake: verify → inbox (unique `event_id`) → stage → single save → 200
- [ ] Handler fetches current state, is idempotent, updates Square-owned fields only
- [ ] Tests above pass; `@integration-reviewer` and `@outbox-reviewer` run
