# 0009 — Redis for session tickets (Azure hosting = DECIDE)

- **Status:** Redis as the session store is **Accepted**. Azure hosting is **DECIDE** (resolve in prompt 6.1).
- **Date:** 2026-10-19
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`, `.claude/rules/aspire.md`
- **Adopts:** creatorpantry **B-02** (Redis-backed session tickets) and **B-04** (Redis as a required dependency)

## Context

ADR 0002 keeps BFF session tickets server-side, so logout and revalidation can revoke them. The gateway reads the ticket on every authenticated request. The API also uses Redis as the HybridCache L2. Locally, Redis is a container. Azure hosting is not yet chosen, and the option the architecture doc priced ("$15–20/mo managed") has changed:

- **Azure Cache for Redis (Basic, Standard, Premium) is retiring.** All instances retire on 2028-09-30 and are disabled from 2028-10-01. Microsoft recommends Azure Managed Redis instead ([retirement FAQ](https://learn.microsoft.com/azure/azure-cache-for-redis/retirement-faq), updated 2026-08-18).
- **New Basic/Standard/Premium caches are reportedly no longer creatable** (new customers from 2026-04-01, existing customers from 2026-10-01). I couldn't confirm these dates on a Microsoft page. **UNCONFIRMED: check before 6.1.**

## Decision (accepted part)

- **Redis stores gateway session tickets.** Redis unavailability means sign-in is unavailable, and health checks show it.
- **Run mode stays a local Redis container.** No Azure account is needed (ADR 0008).

## Options considered (DECIDE: Redis hosting in Azure)

Prices are list prices for East US from the [Azure retail prices API](https://prices.azure.com/api/retail/prices), fetched 2026-10-19, using 730 hours a month. Confirm them in the pricing calculator during 6.1.

| Option | Monthly cost (dev / prod) | Trade-offs |
|---|---|---|
| **A. Azure Managed Redis, Balanced B0 (1 GB)** | ~$11.70 per instance ($0.016/h). Confirm whether HA vs non-HA changes it. | Managed patching, Entra ID auth, zone redundant by default. Microsoft footnotes B0/B1 as possibly lacking some features. Aspire: `AddAzureManagedRedis("cache").RunAsContainer()` keeps run mode a container ([aspire.dev](https://aspire.dev/integrations/cloud/azure/azure-cache-redis/azure-cache-redis-host/)). |
| **B. Azure Managed Redis, B1 (1 GB)** | ~$23.40 ($0.032/h) | Same as A, with fewer B0 feature caveats. |
| **C. Redis as a container app** (0.25 vCPU / 0.5 GiB, min 1 replica) | ~$5.90 at idle rates, up to ~$19.70 if always active ($0.000003 idle and $0.000024 active per vCPU-s; $0.000003 per GiB-s). Less if the subscription's monthly free grant isn't used up by other apps. | Cheapest when idle, but we patch the image, set the password, keep TCP ingress internal-only, and decide on persistence. A restart without a volume signs everyone out. |
| **D. No Redis in Azure: session tickets in SQL** | $0 extra | No extra resource, but it departs from B-02/B-04, adds a SQL read per authenticated request, and loses the HybridCache L2. It needs its own ADR superseding this one. |

**Not an option:** new Azure Cache for Redis Basic C0 (the "$15–20/mo" in the old estimate). It's retiring, and new instances reportedly can't be created.

## Consequences

- **Whichever option is chosen,** the gateway's ticket store and the API's HybridCache L2 use the `cache` reference from service discovery. No code changes between options A–C.
- **Options A/B change the Aspire call** to `AddAzureManagedRedis` for publish mode. That's a 6.1 change and must be verified on aspire.dev then.
- **Update `docs/architecture/architecture.md`** (Redis cost line and the Azure diagram) when this is decided.
