---
name: add-background-job
description: Add scheduled or long-running 6311arts work in Arts6311.Worker — scheduled publishing, reconciliation, imports, cleanup — built on the outbox and durable operation records.
---

# Add a Background Job

There are two kinds:

- **Triggered by a commit.** That's an outbox message. Use `add-outbox-message`.
- **Triggered by time or by staff** (scheduled publish, reconciliation, cleanup, import). Use this skill.

1. **Choose the trigger.**
   - A `PeriodicTimer` hosted service in the Worker, for short sweeps: scheduled publish every minute, reconcile every 15 minutes, nightly full reconcile.
   - Or a staff command that returns `202` and an `Operation` record (import, full reconcile on demand).
2. **Run it safely on more than one replica.** Claim work with a guarded update (the same concurrency-token technique as the outbox). Two replicas must never both do the same unit.
3. **Do the work through facades** in a fresh DI scope per pass. A job never touches `DbContext`, repositories or gateways directly.
   - The exception is shared-kernel housekeeping, which no module owns. It goes through the kernel's own port, for example `IIdempotencyMaintenance` (ADR 0020).
4. **Make each unit small and idempotent.**
   - Publishing a scheduled item is one facade call, and it stages its own outbox messages.
   - A reconcile correction is one save plus an audit row.
5. **Persist progress** for long operations: `Operation` with status, counts, checkpoint, last error code, and started/finished timestamps. The admin can view it.
6. **Telemetry.** One activity per pass, with counters (claimed, done, failed). Log identifiers only.
7. **Cancellation.** Honour the stopping token between units. Never leave a half-applied unit; rely on the single save.

## Tests

- Two runners racing claim each unit once
- Crash between units resumes from the checkpoint
- Cancellation leaves no partial state
- Each unit is idempotent when re-run

## Checklist before done

- [ ] Trigger chosen and justified; commit-triggered work uses the outbox instead
- [ ] Guarded claim, facades only, idempotent units, checkpointed operations
- [ ] Telemetry and audit; tests above pass; `@outbox-reviewer` run if it stages messages
