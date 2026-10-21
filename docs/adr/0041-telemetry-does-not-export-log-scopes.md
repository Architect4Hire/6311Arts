# 0041 — Telemetry does not export log scopes

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`
- **Builds on:** [0019](0019-correlation-id-is-the-w3c-trace-id.md) (the trace id *is* the correlation id),
  [0036](0036-revalidation-scheduled-publishing-and-previews.md) (the preview token travels in the path)

## Context

ADR 0036 put a preview link's token in the URL path, because the site's server reads it while rendering and
cannot be handed a URL fragment the way an emailed link can. `auth.md` accepts that as the one token in a URL,
on the condition that it never reaches a log or an exported span — and `ServiceDefaults` redacts the segment
after `/preview/` from the `url.path` span attribute to keep that promise.

The 2.13 security review found the promise only half kept. `ConfigureOpenTelemetry` also set
`IncludeScopes = true`, and ASP.NET Core's hosting scope carries `RequestPath` for the whole request. So every
log record written while serving `GET /api/v1/site/preview/{token}` — EF Core's `Executed DbCommand` at
Information among them, and a preview read does query the database — was exported with a live capability URL
attached. Anyone who could read the Aspire dashboard, or Log Analytics once deployed, could replay the link for
its 30 minutes. The gateway had a second route to the same leak: YARP's forwarder logs `Proxying to {targetUrl}`
at Information, and that URL is the one with the token in it.

A log *processor* cannot fix this. Scopes are an `ILogger` construct that the SDK walks at export time through
`ForEachScope`; `LogRecord.Attributes` can be rewritten, scopes cannot. Nor can middleware rewrite
`HttpContext.Request.Path` — the route would stop matching.

## Decision

**Log scopes are not exported.** `IncludeScopes` is `false` in `ServiceDefaults`, for every host.

The gateway additionally sets `Yarp` to `Warning`, so the forwarder's `Proxying to {targetUrl}` line — which
carries the token in the message itself, where no scope setting would help — is not written at all.

## Options considered

| Option | Cost |
|---|---|
| **Not exporting scopes** (chosen) | Loses `RequestId` and `RequestPath` as automatic attributes on every record. |
| Redact in a log processor | Does not work: a processor cannot reach the scope stack. |
| Raise EF's command logging to `Warning` | Mitigation, not a fix. Any future Information record during a preview request leaks again, and the next person to add one has no way to know. |
| Take the token out of the path | Reverses ADR 0036. The site's server has nowhere else to read it from. |
| Accept it | `auth.md` says a token is never a log line. A 30-minute read of one draft record is a small prize, but the control either holds or it does not. |

## Consequences

- **Little is actually lost.** OpenTelemetry stamps `TraceId` and `SpanId` on every record from the ambient
  `Activity`, and ADR 0019 already makes that trace id the correlation id — so tying a log line to its request,
  and to the span that holds the (redacted) path, never depended on the scope.
- **What a log line needs, it must say itself.** New logging puts the ids it wants in its own message template.
  `BeginScope` is not forbidden, but nothing exported will show it, so it is not the place to put context.
- **A test pins it.** `TelemetryRedactionTests` asserts `IncludeScopes` is false after `AddServiceDefaults`,
  beside the existing assertions on `RedactSecretSegments`. Without a test this is one careless line from
  returning, and the leak is invisible from the outside.
- **The redaction list stays closed.** `SecretPathSegments` is still `["preview"]`, and `auth.md` still requires
  an ADR to add to it.
- **Deployment inherits it.** 6.1 configures no log collector that re-enables scopes.
