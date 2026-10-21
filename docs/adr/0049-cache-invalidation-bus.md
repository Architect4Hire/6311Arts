# 0049 — A cache invalidation bus: the Worker's revalidations reach the API

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/aspire.md`
- **Amends:** [0036](0036-revalidation-scheduled-publishing-and-previews.md) (how an invalidation reaches another
  process)
- **Builds on:** [0042](0042-site-read-api-and-its-cache.md) (the site reads and their tags)

## Context

ADR 0036 had the Worker's `site.revalidate-requested` handler clear cache tags in HybridCache. Because the Worker and
the API share a Redis L2, the ADR assumed the API would treat its L2 copies as stale, and that its in-memory L1
(capped at one minute) was the only delay. Content.md promised a change on the site within a minute.

Prompt 3.4's checkpoint measured it through the running stack, by editing, unpublishing and republishing a class in
the admin:

| Step | Site page |
|---|---|
| Edit a published class | 78 s |
| Unpublish → 404 | 103 s (slug read), 179 s (the class list the page reads) |
| Publish again | 291 s (list) |

Each read changed when its own Redis entry expired, the five-minute `SiteCachePolicy.Lifetime`. The cause is in
HybridCache ([source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.TagInvalidation.cs)):

- each process reads a tag's invalidation time from L2 **once**, the first time it meets the tag, and keeps it;
- only a `RemoveByTagAsync` in the same process replaces that time.

So the Worker's invalidation was written to Redis and never read by the API. The library has no cross-process
invalidation of its own. Every test passed because each used one process.

## Decision

**A cache invalidation bus over Redis pub/sub.** It lives in the kernel's `Managers/Caching/`, on the connection
HybridCache's L2 already uses:

- **Publishing:** `HybridAppCache.RemoveByTagAsync` clears the tag as before (locally and in L2), then publishes it
  through `ICacheInvalidationBus`. A failed publish throws, so the outbox retries the revalidation; clearing a tag
  twice is harmless.
- **Listening:** `CacheInvalidationListener`, a hosted service, subscribes before the host serves. It applies each tag
  another process published to this process's HybridCache, which drops the L1 copies and replaces the tag time the
  process was keeping. It calls HybridCache directly, not `IAppCache`, so applying a tag never publishes it again.
- **The channel:** `RedisCacheInvalidationBus`, on `6311:cache:tag-invalidated`. A message is the sending process's
  id, a line break and the tag; a process skips its own. A message queue rather than a callback, so tags are applied
  one at a time, in order.
- **Wiring:** `AddArtsCacheInvalidation()` is called by the API and the Worker after
  `AddRedisDistributedCache("cache")`, whose `IConnectionMultiplexer` it uses. Every other host (MigrationService,
  tests) keeps `NoCacheInvalidationBus`.
- **A host that cannot subscribe does not start.** Serving without the subscription would bring the five-minute
  delay back silently.

**Delivery is at most once.** A message missed while a process is disconnected leaves its copies no older than their
lifetime, which is the old behaviour as a worst case rather than the normal one. A process that restarts reads every
tag's time from L2 afresh.

## Options considered

- **(A) Redis pub/sub, on the existing connection.** Chosen. It needs no new infrastructure, reaches every API
  replica, and the outbox stays the only way work leaves a commit: the bus carries cache hints, not effects.
- **(B) Shorten `SiteCachePolicy.Lifetime` to a minute.** A one-line change, but the worst case would be about two
  minutes (L2 plus L1), every reader would hit SQL five times as often, and the 0036 guarantee would still be wrong.
- **(C) Replace HybridCache with a library that has a backplane (FusionCache).** It solves the same problem with a new
  dependency, and every facade's cache calls would change.
- **(D) Have the API run the revalidation handler.** The outbox is drained by the Worker (ADR 0003), and several API
  replicas would still not hear each other.

## Consequences

- **Measured after the fix** (prompt 3.4), caches warm before each step:

  | Step | Before (site page) | After: admin in the browser (API / site page) | After: E2E test (site page) |
  |---|---|---|---|
  | Edit a published class | 78 s | 6.0 s / 6.0 s | 5.4 s |
  | Unpublish → 404 | 179 s | 2.5 s / 2.5 s | 4.3 s |
  | Publish (again) | 291 s | 0.1 s / 4.3 s | 4.2 s |
  | Undo the edit | 149 s | 4.3 s / 4.3 s | — |

  The rest is the Worker's five-second poll. The `Category=E2E` test
  `APublishedClassReachesTheSitePageAndUnpublishingMakesIt404` bounds each step at 30 seconds and logs the times.
- **New tests:**
  - `CacheInvalidationTests` (two service providers sharing one L2 and a bus) fails without the listener, and its
    control case shows the shared L2 alone serves the old copy.
  - The site's class page spec asserts the 404 status for a class missing from the list.
- **StackExchange.Redis** is now a direct dependency of the Domain, pinned to the version Aspire's Redis client
  resolves, and named only by `Managers/Caching/RedisCacheInvalidationBus.cs` (backend.md).
- **`HybridAppCache.MaxLocalLifetime` stays,** but it is not what bounds cross-process staleness: the bus is, and a
  missed message is bounded by the entry's lifetime.
- **`RemoveAsync(key)` is not published.** Its one use is `GetOrCreateFoundAsync` dropping a not-found answer the
  same process has just written. A key-based removal another process must see would need the same treatment.
