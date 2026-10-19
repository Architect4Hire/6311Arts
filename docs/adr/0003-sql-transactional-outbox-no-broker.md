# 0003 — SQL transactional outbox, no broker

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/outbox.md`, `.claude/rules/backend.md`, `.claude/rules/square.md`, `.claude/rules/external.md`
- **Adopts:** the creatorpantry shared-kernel outbox (`Managers/Outbox/`), as-is. It has no B-number in creatorpantry's baseline.

## Context

Many CMS saves must cause work elsewhere after they commit: Square catalog and inventory pushes, emails, media renditions, SSR/cache revalidation, member-status notifications. Inbound Square webhooks must be acknowledged quickly and processed reliably. If the domain write and "do the side effect" can come apart, the result is lost pushes or ghost pushes.

## Decision

- **The outbox table in `ArtsDbContext` is the queue.** A DataLayer stages messages with `IOutboxWriter.Enqueue` and then makes the **single** `SaveChangesAsync`, so the entity row and its messages commit together or not at all.
- **`Arts6311.Worker` drains it.** It claims rows with leases (concurrency tokens; expired leases are reclaimed), runs one keyed handler per `Type`, retries with capped doubling backoff, and poisons a message after `MaxAttempts`. Poisoned messages wait for an audited Admin replay or discard.
- **Payloads carry ids only.** Handlers are idempotent; Square calls use the outbox message `Id` as the idempotency key.
- **Webhooks:** verify the signature, insert into the `SquareWebhookEvent` inbox (unique `event_id`), stage `square.webhook-received`, save, return 200.
- **Nothing calls a provider inline after a save.**

## Options considered

| Option | Cost / trade-off |
|---|---|
| **SQL outbox as the queue (chosen)** | Atomic with the domain write by construction. One durable mechanism; nothing extra to run locally or pay for in Azure. Throughput is bounded by SQL polling, which is ample for this workload. |
| Azure Service Bus + emulator (v1 plan), with an outbox relay | Mature broker features, but two durable mechanisms, an emulator in run mode, an extra Azure resource and cost. Still needs an outbox for atomicity. |
| Inline provider calls after `SaveChangesAsync` | Simplest, but a crash or timeout between commit and call loses or duplicates work. No retry or poison handling. |
| Hangfire / background-job library | Gives retries and dashboards, but adds its own storage schema and doesn't by itself make the job atomic with the domain write. |

## Consequences

- Every after-commit effect is a message type with a typed event record and a keyed handler (`add-outbox-message` skill).
- Delivery is at-least-once, so every handler must be idempotent.
- Atomicity and idempotency tests run against a real SQL Server.
- One trace runs CMS save → message → Worker → Square through `CorrelationId`.
