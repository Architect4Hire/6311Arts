# 0051 — One component per file, one folder per component

- **Status:** Accepted
- **Date:** 2026-10-21
- **Rules affected:** `.claude/rules/frontend.md`, `.claude/rules/design-system.md`,
  `.claude/skills/new-component/SKILL.md`
- **Amends:** [0050](0050-component-markup-and-styles-in-sibling-files.md) (its multi-component naming clause)
- **Builds on:** [0025](0025-ui-library-tokens-and-primitives.md), [0027](0027-admin-shell-and-staff-sign-in-page.md)

## Context

ADR 0050 split every component's markup and styles into sibling files. That left two problems it had explicitly
worked around rather than solved.

**Six files each declared several components.** `pages/info-pages.ts` held five, `shared/cards.ts` four,
`pages/artists.ts`, `pages/instructors.ts`, `pages/shop.ts` and `lib/status-chip/status-chip.ts` two apiece. ADR
0050 coped by naming their extracted markup after the class (`class-card.html` beside `cards.ts`), which meant a
folder listing no longer told you which markup belonged to which component.

**On the site, those bundles also crossed the lazy-route boundary.** The five components in `info-pages.ts` were
five separate lazy routes, so `/about`'s chunk carried Memberships, Visit, Submit and CmsPage too — one 13.12 kB
chunk (4.03 kB transferred) served any one of them. `/shop` and `/shop/:slug` shared an 11.63 kB chunk; the
`/artists` and `/instructors` pairs were the same. `app.routes.ts` asked for one class out of a module and got
all of them.

**This was a smell rather than a performance problem, and it is worth being exact about why.** The site is
server-rendered, so a route's chunk is not on the critical path for content: the HTML arrives rendered, and
`/about`, `/visit` and `/submit` are `RenderMode.Prerender` — static at build time. The chunk is downloaded to
hydrate (`provideClientHydration(withEventReplay(), withIncrementalHydration())`), so its size touches
time-to-interactive, not first paint, SEO or LCP. And the sums are tiny: the whole `info-pages` chunk was 4.03 kB
transferred. Splitting it saves about a kilobyte on `/shop`, which is milliseconds. The reason to split is
findability and the one-component-per-file rule; the bundling is a consequence worth tidying, not the motive.

**And the flat folders had grown past scanning.** `site/src/app/shared/` held 23 files at one level and
`pages/` 44, with a component's `.ts`, `.html`, `.scss` and `.spec.ts` interleaved alphabetically with its
neighbours'. `projects/ui/src/lib/` was already one folder per component and did not have this problem, so the
repo contained the answer and did not apply it.

## Decision

### One component per file

A `.ts` file declares exactly one `@Component`. Non-component exports stay with the component that owns them:
`ShowMoment` with `ShowCard`, `exhibitionStatus` with `ExhibitionCard`, `StatusTone` with `UiStatusChip`,
`SYNC_STATUS` and `SyncStatus` with `UiSyncStatus`, and `CATEGORIES`/`PAGE_SIZE`/`SORTS`/`plainClick` with `Shop`.

**No barrel file re-exports a group.** A barrel would quietly undo the chunk separation: `pages/classes` needs
`ClassCard` and `ShowCard` only, and reaching them through a `shared/cards` barrel would pull `ProductCard` and
`ExhibitionCard` into the classes chunk. The cost of that is small, but a barrel also hides which component a
page actually uses, which is the thing this ADR is for. Consumers import the component's own module.

### One folder per component

A component owns a folder named after it, holding its `.ts`, its `.html`, its `.scss` when it has styles, and its
`.spec.ts` when the spec covers that component alone:

```text
shared/layout/header/header.ts        pages/product-detail/product-detail.ts
shared/layout/header/header.html      pages/product-detail/product-detail.html
```

Three kinds of file stay outside a component folder:

- **A spec that covers several components** stays beside the area it covers: the admin area specs
  (`features/artists/artists.spec.ts`, `features/media/media.spec.ts` and the other eight) and
  `features/layout/shell.spec.ts`, which also tests `sections.ts`.
- **Directives and pipes**, which are not components: `shared/format.ts` (`MoneyPipe`, `WhenPipe`, `today`) and
  `shared/reveal.ts` stay at the root of `shared/`.
- **The root component.** `app/app.ts` and `app/app.html` stay at the root of `app/`.

A component whose folder is already named after it does not gain a second level: `features/dashboard/dashboard.ts`
stays as it is, and so do `forbidden`, `not-found`, `unavailable` and `section-placeholder`.

### Shared components are grouped by kind, features by area

`shared/` gets a category layer where a category has real members, and none where it would hold one thing:

```text
site/src/app/shared/
├── layout/   header, footer, staff-access
├── cards/    class-card, show-card, product-card, exhibition-card
├── forms/    email-request-form, newsletter
├── media/    art-image
├── hero/  contact/  account-panel/      (no category: they belong to no group)
├── format.ts  reveal.ts                 (a pipe module and a directive)

admin/src/app/shared/
├── pickers/  artist-picker, instructor-picker
├── preview-link/  rich-text-editor/  schedule-control/
```

`features/<area>/` keeps its areas and gains a folder per component inside them. In `projects/ui`, `UiSyncStatus`
moves out of `lib/status-chip/` into `lib/sync-status/`, so the library is one folder per component throughout.

### This amends ADR 0050's naming rule

ADR 0050 said an extracted file takes the `.ts` stem "or the kebab-cased class name when one file declares
several components". That second clause is withdrawn: no file declares several components, so a component's
files always share its folder's name. The rest of 0050 stands.

## Options considered

- **Leave the multi-component files alone and only add folders.** Cheaper, but a folder named `cards/` holding
  four components' worth of interleaved files is the flat-folder problem one level down, and it would have left
  the lazy-route bundling in place.
- **Split the files but keep the folders flat.** Separates the chunks and fixes nothing else; `pages/` would have
  gone from 44 files to 54.
- **Add a barrel per group** (`shared/cards/index.ts`) so consumer imports stay short. Rejected: it re-couples
  the chunks and hides which card a page uses.
- **A category layer for every shared component**, including `hero`, `contact` and `account-panel`. Rejected: a
  category with one member is a folder that answers no question.
- **Split the two-component specs** (`shop.spec.ts`, `status-chip.spec.ts`) so every spec sits inside a component
  folder. Rejected: both share a setup block and a test host, and duplicating those to satisfy the layout would
  make the tests worse. `shop.spec.ts` lives in `pages/shop/` and imports `../product-detail/product-detail`.

## Consequences

- A component is one directory: its markup, styles, spec and class in one place, found by name. That is the
  benefit this ADR is claimed on.
- **Each lazy route now ships only its own code**, which is a small win and not a speed one: `/shop` went from a
  4.03 kB transfer to 2.79 kB with `product-detail` as its own 1.92 kB chunk, and the five routes that shared
  `info-pages` each have their own. Under SSR this changes hydration weight, not what the visitor sees. The more
  useful part is cache granularity across deploys — editing About no longer invalidates the chunk that Memberships,
  Visit, Submit and CmsPage were served from — and modulepreload hints that name only the route's own code.
- `withIncrementalHydration()` is configured but no template uses `@defer`, so nothing is deferred today and every
  route's chunk is fetched to hydrate. If `@defer (hydrate …)` blocks are added later, per-component files are what
  let a block's code be split off on its own.
- Imports are longer and more numerous — `../shared/cards` became three imports in `pages/home` — and they now
  say which component is being used, which is what makes the chunking work.
- 203 files moved and 454 import specifiers were rewritten by resolving each one against its file's old directory
  and re-relativising from the new one; a pass then confirmed every relative import, `templateUrl` and `styleUrl`
  resolves to a real file. `npm run build` is clean and `npm test` passes 598 tests across 58 files, unchanged.
- **What we must not do now:** declare two `@Component`s in one file; add a barrel that re-exports a folder of
  components; put a single-component spec outside its component's folder; create a category folder for one
  component; or nest a component folder inside a folder that already bears its name.
