# 0019 — The correlation id is the W3C trace id

- **Status:** Accepted
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/outbox.md` ("`CorrelationId`"), `.claude/rules/backend.md` ("Contracts"), `.claude/rules/gateway.md` ("Limits and correlation"), `.claude/rules/api-contract.md` ("Errors")

## Context

`OutboxMessage.CorrelationId` and `AuditLog.CorrelationId` are `Guid` columns that must tie a row back to the request that wrote it, so one trace runs CMS save → message → Worker → Square (ADR 0003). `ProblemDetails` also return a `correlationId` (api-contract.md), and the gateway must create or propagate one (gateway.md). Prompt 1.4 needs something a DataLayer can pass to `IOutboxWriter.Enqueue` before the gateway or the API conventions exist.

OpenTelemetry already gives every request a W3C trace id through ServiceDefaults, and YARP forwards `traceparent`, so a gateway → API request has a single trace id. A W3C trace id is 16 bytes: exactly a `Guid`.

## Decision

- **`ICorrelationContext.Current` (shared kernel, `Managers/Correlation/`) is the current `Activity`'s trace id as a `Guid`,** read big-endian so `correlationId.ToString("N")` equals the trace id's hex. Searching the dashboard or App Insights for a row's `CorrelationId` finds its trace.
- **With no W3C activity** (a test, a CLI with no listener), it is one random id fixed per DI scope, never `Guid.Empty`.
- **A DataLayer passes `ICorrelationContext.Current`** to `Enqueue` and to `AuditEntry`. `IOutboxWriter` rejects `Guid.Empty`.
- **There is no separate correlation header.** The gateway (1.8) and `ProblemDetails` (1.7) report the trace id in this same form rather than minting a second id.

## Options considered

| Option | Cost / trade-off |
|---|---|
| **W3C trace id as the correlation id (chosen)** | No new header and no middleware; it already flows through YARP and every OpenTelemetry-instrumented hop, and the stored id *is* the trace key. It depends on tracing being on, hence the per-scope fallback. |
| A separate `X-Correlation-Id` header, minted by the gateway | A second id to propagate, store and reconcile with the trace id. Lets a client supply its own id, which we don't need and would have to validate. |
| A new `Guid` per enqueue | Ties nothing together: useless for following one save across hops. |

## Consequences

- **Still open for 1.5:** the Worker must continue the enqueuing trace when it dispatches. The dispatcher doesn't do that yet (1.4 ships the kernel only). 1.5 starts the handler's activity with the message's `CorrelationId` as its trace id (an `ActivityContext` parent built from it) and makes a handler's follow-up `Enqueue` calls inherit it. Until then, handler work appears as its own trace, linked only by the stored id.
  - **Amended in 1.5 (how, not what):** a parent built from `CorrelationId` alone names no real span, so the handler would show as an orphan under a missing parent.
    - `OutboxMessage` now also stores the enqueuing span's W3C `TraceParent`, and the dispatcher's `outbox.process {Type}` consumer span is its child.
    - The correlation-id parent remains the fallback when nothing was tracing at enqueue.
    - The Worker's poll passes run under an unrecorded activity, so idle polling exports no spans.
    - The decision above is unchanged.
- **A correlation id is never an idempotency key.** One request can stage several messages, and they all share one correlation id. Handlers key on the outbox message `Id` (outbox.md).
- **Must not:** generate a correlation id anywhere except `ICorrelationContext`, or put anything but the trace id in `ProblemDetails.correlationId`.
