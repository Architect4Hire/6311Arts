# 0036 — Revalidation by cache tag, the scheduled publisher, and preview links

- **Status:** Accepted — the preview answer's shape amended by [0042](0042-site-read-api-and-its-cache.md)
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/outbox.md`, `.claude/rules/aspire.md`,
  `.claude/rules/backend.md`, `.claude/rules/api-contract.md`
- **Builds on:** [0003](0003-sql-transactional-outbox-no-broker.md) (the outbox),
  [0009](0009-redis-for-session-tickets.md) (Redis), [0020](0020-idempotency-scoped-to-a-server-owned-subject.md),
  [0023](0023-identity-module-and-email-through-the-outbox.md) (the shared Data Protection key ring),
  [0031](0031-artists-and-the-shared-content-lifecycle.md), [0033](0033-exhibitions-and-cross-module-artist-credits.md),
  [0034](0034-html-sanitizer-and-pages.md), [0035](0035-redirects-and-slug-changes.md)

## Context

Prompt 2.8 owes three things, and clears a debt three ADRs left:

- **Revalidation.** Every status command must stage `site.revalidate-requested {entityType, entityId, paths}` in its
  own save, and a Worker handler must invalidate the matching HybridCache tags — with no HTTP call to the site.
  ADRs 0031, 0033 and 0034 each left a skipped test (`PublishStagesARevalidationInTheSameSave`) and content.md
  carried a dated exception saying status commands stage nothing yet.
- **Scheduled publishing.** `PublishStatus.Scheduled` and a `ScheduledFor` column have existed since 2.4 with
  nothing to reach them: there was no `schedule` command, and nothing published what was due.
- **Previews.** Staff need to see an unpublished record as the site will show it, through a signed, expiring link
  that grants no write access and never enters the sitemap.

ADR 0035 also left two consequences here: revalidating `type:redirect` on a redirect change, and the site's
five-minute staleness.

## Decision

1. **`site.revalidate-requested` carries the paths, and that is the second documented exception to "payloads are
   ids only".**
   - The event is `SiteRevalidateRequested(EntityType, EntityId, Paths)` in `Managers/Outbox/Events/`, beside
     `email.send-requested`, because every content module writes it and one kernel handler applies it.
   - The reasoning is ADR 0035's: a path is a public URL, not content, a price, a token or anyone's data, and the
     address a record was published at when the transition committed cannot be recovered by reloading it later —
     the next edit may move it.
   - `TryParse` rejects everything the tag builders would throw on (an empty type, a `:` inside a segment, an
     unrooted path, more than `MaxPaths`), so a malformed payload is a poisoned message on its first attempt
     rather than an exception five times over.
2. **Invalidation happens in the kernel, by tag, and never over HTTP.**
   - `Managers/Revalidation/` holds `ISiteRevalidationFacade`, its implementation and the keyed handler, with
     `AddArtsRevalidation()`. No module owns the cache, which is why this sits beside `Managers/Email/`'s
     delivery rather than inside a content module.
   - One event clears three kinds of tag: `type:<entityType>`, `entity:<entityType>:<id>` and `site:<path>` for
     each path. A list page carries the type tag and an item page the entity and path tags, so either kind of
     change clears what showed the record.
   - The handler reloads nothing: the event is the fact that something changed, and the next reader runs the
     factory. That is what makes a replayed or out-of-order message harmless.
3. **The API and the Worker share Redis as HybridCache's L2, and up to a minute of staleness is accepted.**
   - Both hosts call `AddRedisDistributedCache("cache")`, and the AppHost gives both a reference to `cache`.
     Without a shared out-of-process cache the Worker's invalidation would reach only its own process, and the
     handler would be the no-op ADR 0031 refused to build.
   - HybridCache's tag invalidation is *logical*: it records "ignore anything tagged this and created before now"
     in the calling host and in L2, so another host treats its L2 copy as a miss. It cannot reach another host's
     in-memory L1, which keeps its copy until `LocalCacheExpiration`. `HybridAppCache` caps that at
     `MaxLocalLifetime` (one minute), so **a published change shows on the site within a minute at worst.** That
     is the documented ceiling, and it replaces ADR 0035's five minutes for redirects.
   - Known risk: `dotnet/extensions#7771` reports that an instance which has never resolved a tag's invalidation
     timestamp can serve an invalidated L2 entry shortly after startup or scale-out. The one-minute L1 cap bounds
     it; if it is ever seen, the fix is upstream.
   - **Accepted risk, flagged for a follow-up prompt: `cache` is one Redis instance, and it already holds the
     gateway's session tickets and its Data Protection key ring (ADR 0009, 0024).** The API was always meant to
     use it as an L2 (aspire.md's graph), and now the Worker does too, so both hosts hold a credential for the
     store that keeps session material. Key prefixes stop accidental collisions, but they are not isolation: a
     compromised API or Worker could read or overwrite tickets, the ring, or a cached page. **DECIDE (owned by
     6.1, the publish model):** give the content cache its own Redis resource, logical database or ACL user, and
     encrypt the gateway's key ring at rest. Nothing in 2.8 depends on which way that goes.
4. **Every status command stages a revalidation, and so does every redirect write.**
   - The three content DataLayers stage it in `TransitionAsync` (and in `AbandonScheduleAsync`), before the single
     save, with the record's own address as the one path. Schedule and unschedule stage it too: uniform is simpler
     than deciding which transitions could matter.
   - `RedirectsDataLayer` stages one message per row a write or a slug change touches, with the addresses at both
     ends of the hop when the destination is a site path (ADR 0035's consequence). The legacy seed does not: it
     runs in MigrationService, before any host has cached anything.
   - content.md's dated exception is removed, and the three skipped tests are now real.
5. **`schedule` and `unschedule` join the shared lifecycle.**
   - `PublishLifecycle`: Draft or Scheduled → Scheduled (a reschedule moves the time, which is a real write, not a
     no-op), Scheduled → Draft. A published record is unpublished first, deliberately.
   - `schedule` is the one command with a body, so each module's facade gives it a method of its own rather than
     another `TransitionAsync` argument. The body is the kernel's `ContentScheduleViewModel` — one shape, so the
     API has one name for it — and "a time in the future" is a rule in Business, checked against `IClock`.
   - **What publishing requires is checked at schedule as well as at publish** (a portrait's alt text, an
     exhibition's summary, a page's real body), so staff hear about it while they are looking at the screen rather
     than at midnight.
   - Archiving clears `ScheduledFor`: a time left on an archived record would only mislead.
6. **The scheduled publisher claims by row version, and needs no lease.**
   - `ScheduledPublishHostedService` in the Worker runs a pass at startup and then every
     `ScheduledPublishPolicy.Interval` (one minute), in a fresh DI scope, through the kernel port
     `IScheduledPublisher` that each publishable module implements over its facade.
   - Each unit re-reads its record and saves with the row version it just read. The row version is a concurrency
     token, so of two Workers acting on one record exactly one save succeeds and the other is told it lost. Nothing
     is claimed up front, so a Worker that dies mid-pass leaves no held work: the next pass reads the due list
     again, which is also the crash-recovery path.
   - **A record that can no longer be published goes back to Draft** with its time cleared and an audit row of its
     own (`<type>.schedule-abandoned`), rather than being retried every minute for ever with nobody told.
   - Cancellation is honoured between units; a unit is one save, so nothing is half-applied. A failed pass is
     logged and the next tick tries again.
   - **A unit that throws is caught, counted and logged, and the pass carries on.** The due list is ordered by
     time, so a record that fails every time would otherwise be retried first every minute and starve everything
     behind it. A failed read of the record itself (Media unreachable, say) is raised rather than reported as
     "nothing due", so it is counted as a failure instead of repeating in silence.
   - **Each unit gets its own scope, and so its own `ArtsDbContext`;** the due lists are read in a scope of their
     own before any unit runs. Sharing one context across a pass would let a unit that threw after staging
     entities leave them tracked, and the next unit's save would re-attempt that failed write and fail with it —
     the very starvation the per-unit catch exists to prevent.
   - Each publish stages its own revalidation in its own save, because it goes through the same facade a person
     uses. The actor on the row is `system:scheduled-publisher`.
7. **Preview links are Data Protection tokens, and the Preview module is the only thing that knows every content
   type.**
   - `POST /api/v1/cms/preview` (`CmsEditor`) returns `{ token, path, expiresAt }`;
     `GET /api/v1/site/preview/{token}` (`[AllowAnonymous]`) returns the record. The token is the credential: the
     site's server fetches this while rendering, and the person following the link has no session.
   - The token is a time-limited Data Protection token (`ITimeLimitedDataProtector`, purpose
     `Arts6311.Preview.v1`) over `type:id`, valid for 30 minutes on the key ring every host shares (ADR 0023). It
     is given a *lifetime*, not an instant from `IClock`: Data Protection validates the expiry it embeds against
     the system clock, and a token's validity must not depend on two clocks agreeing.
   - **Everything unusable is one `resource.not_found`** — expired, tampered with, another purpose's, too long, or
     a record since deleted — so a holder learns nothing from the answer. The response is `no-store` and carries
     `X-Robots-Tag: noindex, nofollow`, and the body carries `noIndex: true` for the page the site renders.
   - A preview grants **no write access**: no write path accepts the token, and the read route is anonymous and
     safe. Nothing puts it in browser storage or a sitemap.
   - **A deliberate, narrow exception to "no tokens in URLs" (CLAUDE.md, auth.md).** Identity's emailed tokens
     travel in the fragment precisely so they never reach a server log (ADR 0029); a preview cannot, because the
     site's server reads the token while rendering the page. It is a capability URL instead, and the exposure is
     contained three ways: the token lives 30 minutes, grants one read, and
     `ServiceDefaults.RedactSecretSegments` replaces the segment after `/preview/` in the `url.path` of every
     exported span, in every host. Request-path *logs* stay out of it because `Microsoft.AspNetCore` logs at
     Warning. A second path segment may only be added to that redaction list by ADR.
   - **It cannot be revoked.** A link keeps working for its 30 minutes after the editor signs out, is logged out
     everywhere, or is disabled. Accepted on the same reasoning as the unaudited mint: it reads one record the
     editor could already open. A shorter life or a revocation list would need its own ADR.
   - **The site's `/preview/{token}` page is 3.x's**, with the rest of the site's reads. It owes `noindex` in the
     rendered HTML, `no-store` from SSR, and absence from the sitemap and any prerender list; the API's
     `X-Robots-Tag` covers only the JSON.
   - `Modules/Preview/` is a module of its own: one token must resolve to any content type, so something has to
     know them all, and it reaches each only through its facade and ServiceModels. It has no Business or Data
     layer — there is no rule to own and nothing to persist — as `Operations` has none below Business.
   - **Minting is not audited.** A link grants one read of a record the editor could already open in the CMS, and
     making one changes nothing, so it writes no row. It would also be the only reason the module needed a
     DataLayer.
8. **The preview returns the CMS ServiceModels for now** (your decision), wrapped in
   `ContentPreviewServiceModel { entityType, entityId, path, status, noIndex, artist?, exhibition?, page? }` with
   exactly one member set. The site's own read models arrive in 3.1 and this shape takes them then. The named
   members keep the contract and its generated TypeScript explicit where a polymorphic union would not.
   - **The CMS's own bookkeeping is emptied in a preview**: `updatedBy` (a staff user id), `eTag` and
     `legacyWordPressId` come back blank or null. Reusing the shape was the decision; disclosing who edited a
     draft to whoever holds the link was not.
9. **The site's path prefixes live in the kernel** (`Managers/Publishing/SitePaths`), as three literals plus
   `AddressOf`, because the preview link needs them from outside a module and may not reach past another module's
   facade (backend.md "Module rules"). Each module's policy keeps its own `SitePathPrefix` and takes it from there,
   so each prefix is written once.
   - **Which content type has which prefix is not in the kernel.** That map is `PreviewPolicy.PathPrefixes`, in the
     one module allowed to know every content type, and it is the same dictionary the allow-list comes from — so a
     type cannot be previewable without an address. Revalidation and redirects never consult the map: they are
     handed the paths by the module that staged them.
10. **Media visibility stays DECIDE, and 3.1 owns it** (your decision). ADR 0030's "Publishing an image" is not
    settled here: nothing anonymous reads content images before Phase 3, so the only cost today is that a preview's
    images are staff-only. ADR 0031's recommendation (option 1, `media.publish-requested` staged by publish)
    stands as the proposal.
11. **One migration, `AddScheduledPublishIndexes`**: `(Status, ScheduledFor)` on Artists, Exhibitions and Pages,
    for the once-a-minute due query. No new columns — `ScheduledFor` has been there since each type landed.

## Options considered

- **An HTTP revalidation call from the Worker to the site or the API.** Rejected: the prompt rules it out, the
  Worker holds no credential the API accepts (the internal token is the gateway's to sign, ADR 0002), and it would
  put a provider call inside a handler whose only job is to forget something.
- **Invalidating by key rather than tag.** Rejected: a change to one artist affects list pages, the artist's page
  and anything that credits them. Tags are what `CacheKeys`/`CacheTags` were built for in 1.6.
- **A lease or claim table for the scheduled publisher** (as the outbox has). Rejected: the row version already
  guards the write, the units are small and idempotent, and a lease would add a second thing to expire. The outbox
  needs one because a handler's side effect is outside the database; publishing's is not.
- **Leaving a record Scheduled when its requirements fail.** Rejected: it retries every minute, writes an audit
  row each time or none at all, and leaves staff with a record that looks scheduled for ever.
- **Scheduling without checking the publish requirements.** Rejected: the refusal would arrive hours later, in a
  log.
- **Previews inside each content module** (`/api/v1/site/artists/preview/{token}`). Rejected: one token cannot name
  its own route, and the prompt specifies one address.
- **A kernel preview facade.** Rejected: it would have to name every module, which the shared kernel may never do.
- **Trimmed per-type preview models now.** Considered and declined for this prompt (your decision): 3.1 defines the
  site's read models, and inventing a third shape in between would be thrown away.
- **Storing the preview token's subject in a table.** Rejected: a signed token needs no row, and a row would need
  cleaning up.

## Consequences

- **A published change shows within a minute**, not instantly: the API's in-memory copy lives out
  `HybridAppCache.MaxLocalLifetime`. Anything that needs to be instant needs a different mechanism and its own ADR.
- **The Worker now composes the content modules** (Artists, Exhibitions, Pages, over Media), because the scheduled
  publisher works through their facades. Its startup cost grows by three module registrations.
- **The API and the Worker both need `cache`.** A run with Redis down means the API caches in memory only and the
  Worker's revalidations fail and retry; content is correct either way, and staleness is bounded by each entry's
  lifetime.
- **2.9–2.11 inherit all of this**: a new publishable type registers an `IScheduledPublisher`, stages
  `site.revalidate-requested` in its transitions, adds its prefix to `SitePaths`, and joins
  `PreviewPolicy.EntityTypes` — with a branch in the Preview facade, which throws rather than 404s if one is
  forgotten.
- **`cms.slug_locked` stays unused** (2.7), and `ContentErrorCodes` keeps it for the contract.
- ~~**A plain content edit stages no revalidation**, only status commands and redirect writes do (the prompt's
  scope). Nothing caches a record's content before 3.1, so nothing can go stale yet; 3.1, which adds the site's
  cached reads, has to decide whether an edit to a published record stages one too.~~ **Closed at the 2.13
  checkpoint**, ahead of 3.1: an edit to a record whose status is `Published` now stages a revalidation in its own
  save, through the module's existing `StageRevalidation`. The architecture and outbox reviews both raised it, and
  the decision 3.1 was left to make had only one safe answer — without it, every content edit would serve stale
  copy from the moment 3.1 turned caching on, which is a bug introduced by omission rather than a feature
  deferred. A slug change is also covered from the redirect's side: the handler's new row stages a revalidation
  for both addresses.
- **Editing a redirect's old address leaves that old address's page tag uncleared**: the message names the
  addresses as they stand after the write. The type tag still clears the site's list, which is the only thing
  cached today, so this is bounded staleness rather than a wrong answer.
- **An address a cache tag cannot hold is left out of the payload** rather than carried: a redirect's old address
  may keep a query (`/?p=123`), and a destination may be another site. Carrying one would poison the message and
  clear nothing at all, so `RedirectsDataLayer` takes the path before any `?` or `#` and drops what is left if it
  still cannot be a tag.
- **Handling `redirects.slug-changed` stages another message**, because the redirect it writes revalidates its own
  addresses. One pass therefore leaves one new Pending message, which the next pass delivers — asserted in
  `RedirectsModuleTests`.
- The preview response changes shape in 3.1 when the site's read models land. That is a deliberate, scheduled
  breaking change to a route no public client reads yet.
