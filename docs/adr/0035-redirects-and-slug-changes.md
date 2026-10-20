# 0035 — Redirects: one hop, slug changes through the outbox, a cached site list

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/content.md`, `.claude/rules/outbox.md`, `.claude/rules/api-contract.md`
- **Builds on:** [0003](0003-sql-transactional-outbox-no-broker.md) (the outbox), [0031](0031-artists-and-the-shared-content-lifecycle.md)
  (the slug lock this replaces), [0033](0033-exhibitions-and-cross-module-artist-credits.md), [0034](0034-html-sanitizer-and-pages.md)

## Context

Prompt 2.7 adds the Redirects module:

- `Redirect` (from, to, code, source)
- Admin-only, audited CMS CRUD
- a cached public `GET /api/v1/site/redirects`
- a Redirects facade method that other modules call when a published slug changes

Loops and chains longer than one hop must be rejected at write time.

Until now, ADR 0031 locked a published slug (`cms.slug_locked`) because nothing could redirect the old address.
Three content types (Artists, Exhibitions, Pages) relax that lock here.

## Decision

1. **Module and entity.**
   - `Redirect` has these fields:
     - `FromPath`: unique.
     - `ToPath`.
     - `TargetPath`: `ToPath` without its fragment, or `null` for another site. It is indexed for the chain check.
     - `StatusCode`: 301 or 302.
     - `Source`: Manual, Imported, or **SlugChange** (new; your decision).
     - `EffectiveAt`.
     - `LegacyWordPressId`, plus the usual stamps and row version.
   - There is no publish lifecycle: a redirect is live once written.
   - Every CMS route is `CmsAdmin` and every change is audited ("redirect changes" are sensitive in auth.md). The
     admin section is Admin-only too.
2. **One normal form for addresses (`RedirectPaths`).**
   - The path is lower-cased and loses a trailing slash; the query is kept. `/Gallery/` and `/gallery` are one
     redirect, and `/?p=123` (a WordPress post address) is allowed.
   - An old address may not be:
     - the home page
     - an address with a fragment
     - an address with `.`/`..` segments (a browser would resolve them past the checks)
     - under a reserved prefix: `/admin`, `/api`, `/bff`, `/media`, `/webhooks`, `/health`, `/alive`, `/signin`,
       `/account`
   - A destination is a site path (which may not point into `/api`, `/bff`, `/webhooks` or the health routes) or an
     `https://` address with a host and no user info.
   - The site (Phase 3) must normalise an incoming path the same way before it looks one up.
3. **Staff writes refuse loops and chains** (`redirects.loop_or_chain`, 409).
   - A redirect may not lead to itself.
   - Its destination may not be another redirect's old address. That covers loops too: A→B then B→A is refused.
   - Its old address may not be another redirect's destination.
   - A taken old address is `redirects.from_taken` (409).
   - Business checks all of these. The unique `FromPath` index catches a race on the old address, and only that one.
     Two Admins saving A→B and B→C in the same instant could each pass the chain check and commit a two-hop chain.
     This is accepted: it takes two Admins acting on complementary redirects within milliseconds, the result still
     works (two hops, no loop), and the next edit to either redirect is refused until it is flattened. Serializing
     redirect writes would close it if it is ever seen.
4. **A published slug change creates its redirect through the outbox (your decision).**
   - Business computes a `PublishedPathChange`. One is owed only when the record has ever been published (its
     `PublishedAt` is set).
   - The content module's DataLayer stages `redirects.slug-changed` in the rename's own save, so the rename and its
     redirect commit together or not at all.
   - The Worker's `RedirectSlugChangedHandler` calls `IRedirectsFacade.ApplySlugChangeAsync`. That is "the Redirects
     facade method other modules call", reached through the outbox rather than inline after a save (outbox.md).
   - The redirect itself appears within one dispatcher pass.
   - `cms.slug_locked` is no longer returned, but stays in the contract. `slugLocked` on the ServiceModels now means
     "published, so changing it redirects". The admin's slug fields are editable, and their hint names the address
     that will redirect.
5. **The payload carries the two paths (your decision): a documented exception to "ids only".**
   - The payload is `{contentType, contentId, fromPath, toPath, occurredAt}`.
   - The old address is an immutable fact of the rename, so no reload can recover it, and a path is a public URL
     rather than content or anyone's data.
   - outbox.md records this as the one exception, and any other needs its own ADR.
6. **Slug changes keep every chain one hop instead of refusing,** because a committed rename can't be refused.
   - A redirect *leaving* the new address goes: the address is live content again. If it is *newer* than the
     rename, the rename is stale, and the old address follows that redirect instead.
   - Redirects *arriving* at the old address are repointed to the destination, keeping any fragment. One that would
     now lead to itself goes.
   - The old address then leads to the destination, unless the destination is the old address itself (renaming
     back) or a newer word on it exists.
   - Every comparison uses `EffectiveAt`: the rename's time for slug changes, the write's time for staff and seeds.
     So renames delivered out of order end the same as in order (tested both ways), and an older rename never
     overwrites a newer staff redirect.
   - Applying the same change twice changes nothing, so redelivery is safe.
   - The handler poisons a malformed payload, and a validation refusal (an address that can't be redirected). It
     retries anything else. It records no terminal state on the last attempt: it calls no provider (outbox.md's
     exception), so the Poisoned message is its visible state.
   - **Accepted gap:** that state is visible only once the Admin Sync screen (outbox replay/discard) exists. Until
     then, a poisoned rename shows only in the Worker's logs and the `OutboxMessages` table.
   - The refusal path is kept narrow: a page can't take a slug the site already answers (`PagePolicy.ReservedSlugs`:
     the reserved prefixes and the other content types' sections), so a page rename never produces an address a
     redirect can't use.
   - Artist and exhibition addresses always sit under their own prefix.
   - A rename on a record that is now Draft or Archived still redirects its old address. The new address answers 404
     until the record is published again. This is accepted: the old address is no worse off than unredirected, and it
     starts working the moment the record is.
7. **The site list is cached for 5 minutes (your decision).**
   - The cache is HybridCache under `redirect:list:site`, tagged `type:redirect`, with
     `Cache-Control: public, max-age=300`.
   - Writes don't clear it. Invalidation must come from `site.revalidate-requested` handlers, never inline after a
     save, and that is 2.8's to build. 2.8 adds the tag invalidation.
   - The route is anonymous (`[AllowAnonymous]`) and returns everything, as a small fixed list (api-contract.md).
     It names no one.
8. **Seed:** the spike's three fixed legacy redirects (`/events`, `/gallery`, `/submit-your-work`), **once per database**
   (the `redirect.seeded` audit entry is the marker), so a redirect staff delete stays deleted, and held to the same
   one-hop rules (a seed that would chain is skipped).
   - They are seeded in every environment as `Imported`, idempotently. That lets the site drop its hard-coded map in
     Phase 3.
   - The pattern rules (`/product/{slug}`, `/artist/{slug}`) depend on which slugs exist, so they stay in the
     site's code.
9. **Precedence (for Phase 3, DECIDE):** live content wins over a redirect from the same address. The slug-change rules keep
   that true for renames. A *new* record published at an address a redirect leaves from is the one case they don't
   cover, and the site must resolve it by serving the content.

## Options considered

- **The content facade calls the Redirects facade after its save.** Rejected: two saves, so a crash loses the
  redirect, and outbox.md forbids an inline call after a save.
- **Stage the redirect on the shared DbContext in the rename's save.** Rejected: another module's rows would join a
  module's save, which is a new pattern the boundary rules don't allow.
- **Ids only, with a slug-history table in each content module.** Declined: more tables to recover a fact the event
  can carry.
- **Pull `site.revalidate-requested` into 2.7.** Declined: 2.8 designs it. Five minutes of staleness is acceptable
  before Phase 3 reads anything.
- **Refuse a slug change that would chain.** Rejected: a rename that has already committed can't be refused, so the
  system collapses the chain instead.

## Consequences

- Every Square-synced type in Phase 5 relaxes its slug lock the same way, with its own `SitePathPrefix` and a
  `redirects.slug-changed` from its DataLayer.
- The Worker now hosts the Redirects module.
- 2.8 adds revalidation of `type:redirect` (and of the content pages) to these writes.
- The importer (4.x) loads WordPress redirects as `Imported` through the Redirects facade's create path, so the same
  loop and chain rules apply. It has to order or flatten its source data accordingly.
