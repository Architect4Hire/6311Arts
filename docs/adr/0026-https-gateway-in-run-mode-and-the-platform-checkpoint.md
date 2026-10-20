# 0026 — HTTPS gateway in run mode, a hermetic end-to-end checkpoint, and CI

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/aspire.md`, `.claude/rules/gateway.md`, CLAUDE.md "Usage"
- **Builds on:** [0001](0001-single-origin-yarp-gateway-bff.md), [0008](0008-run-offline-publish-azure-container-apps.md), [0024](0024-bff-session-tickets-and-edge-antiforgery.md)

## Context

Prompt 1.12 is the Phase 1 checkpoint. Its job is to prove the platform starts from nothing and that the flows
Phase 1 built work through the real hosts, then to make CI prove it on every push.

The first end-to-end run found that they did not. The gateway's only endpoint in the AppHost was plain HTTP, but
its cookies are `Secure` and `__Host-` prefixed (ADR 0024), and ASP.NET Core antiforgery with
`SecurePolicy = Always` throws on a non-HTTPS request. So under `aspire run`, `/bff/antiforgery`, `/bff/login` and
every unsafe browser request returned 500. Every 1.10 test used an in-memory host with an `https://` base
address, so none of them could see it.

Two other forces shaped the test itself:

- The AppHost declares persistent containers with named data volumes. A test that ran the model as-is would attach
  to the developer's own `aspire run` containers and their data.
- Its secrets are generated on first run and persisted to user secrets. A test that let that happen would write to
  the developer's machine.

## Decision

### The gateway serves HTTPS in run mode

- **Run mode:** `WithHttpsEndpoint()` on the ASP.NET Core developer certificate. The health check, and the origin
  the Worker builds email links from, use the same endpoint.
- **Publish mode:** a plain-HTTP target port, unchanged. Azure Container Apps terminates TLS at its ingress and the
  container has no certificate.
- **Still owed by 6.1:** behind that ingress the gateway sees plain HTTP, and the same antiforgery 500 returns unless
  the gateway trusts ACA's forwarded headers (`Gateway:TrustedProxies`, ADR 0022), so that `Request.IsHttps` is
  true.
- **Guard:** `AppHostModelTests` asserts every external gateway endpoint is HTTPS in run mode.

### The end-to-end checkpoint is hermetic

- **The test:** `Arts6311.Tests/EndToEnd`, built on `Aspire.Hosting.Testing` and tagged `Category=E2E`.
- **Secrets:** it supplies every generated parameter as host configuration, fresh per run. That keeps them out of
  the process list.
- **It proves the secrets were used.** After start it checks that each parameter resolved to the test's value. One
  that had not would have been generated and persisted to user secrets.
- **Empty volumes:** `EphemeralAppModel` strips data volumes and persistent lifetimes from every container and
  drops DbGate. `AppHostModelTests` checks the result without a container runtime, and fails if a bind mount ever
  appears, so someone decides whether it carries state.
- **Square:** `Square:Mode` is pinned to `fake`.
- **The outbox leg:** no browser route sends email until `/bff/forgot-password`. Until then the test composes the
  Domain and calls the Identity facade against `appdb`, the way the API would. It asserts, all inside the
  request's trace:
  - the Worker's dispatch
  - the keyed handler
  - exactly one ledger row and one Mailpit message
- The fast in-process version of the same path lives in `EmailThroughTheDispatcherTests`.

### CI has three jobs

- **backend:** `dotnet build -warnaserror`, then the tests without `Category=E2E`.
- **frontend:** `npm ci`, `npm run build`, then `npm test`. If no project has a test target yet, it posts a visible
  warning instead.
- **e2e:** depends on backend, and runs on a Docker-capable runner.
  - It installs the Aspire CLI at the `AspireVersion` in `Directory.Packages.props`, because `AspireUseCliBundle`
    takes DCP from the CLI (ASPIRE010).
  - It puts the developer certificate into the system CA bundle.
  - It runs only `Category=E2E`.

Actions are pinned to commit SHAs, Dependabot bumps them, and checkout keeps no credentials. Nothing deploys from
this workflow.

## Options considered

| Option | Cost and trade-offs |
|---|---|
| **HTTPS endpoint in run mode (chosen)** | Needs the dev certificate on every machine that runs the AppHost, which `aspire run` already expects. Keeps `SecurePolicy = Always` and `__Host-` cookies exactly as production will have them. |
| `SecurePolicy = SameAsRequest` in Development | Fixes the 500 but drops `Secure`, and a browser then rejects every `__Host-` cookie. It would also make Development behave unlike production at the one boundary that most needs to match. Rejected. |
| E2E against the developer's persistent containers | No extra code, but the test reads and writes the developer's data, and its result depends on what was there. Rejected. |
| E2E in the default `dotnet test` only (no separate job) | One job, but the backend job would need Docker and an extra ~2 minutes, and a container failure would hide unit results. A separate job gives both a clean signal. |

## Consequences

- **The URLs change.** `aspire run` opens the gateway on `https://localhost:<port>`, and emailed links use that
  origin.
- **Docker is now needed for a full local `dotnet test`.** The E2E test runs in it, taking about a minute after the
  images are pulled.
- **New flows go into the E2E test.** A prompt that adds a browser-visible flow adds an assertion there. A flow that
  sends email asserts through the ledger and Mailpit, tied to its own correlation id, never "any message".
- **6.1 owes forwarded-header trust for ACA**, or the deployed BFF fails exactly as run mode did.
