# 0025 — `@6311/ui`: tokens, global styles and shared primitives

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/design-system.md`, `.claude/rules/frontend.md` (Workspace config, Verification)
- **Implements:** the `projects/ui` half of the one-workspace decision in `docs/prompts/scrub-prompts.md`, and the `ui` test target [0016](0016-vitest-for-frontend-unit-tests.md) deferred

## Context

The spike's look lived in one 2,200-line `styles.scss`: 16 custom properties on `:root`, about 40 literal colors
after them, and the `.btn` and `.field` classes the site's templates write by hand. The admin (2.2) needs the same
type, palette, buttons and fields, plus a handful of things the site has never needed: a status chip that says
whether a record is synced with Square, a dialog, a notice, an empty state. Two apps copying one stylesheet drift
on the first edit. The constraint on extraction was strict: the public site must not change by a single pixel.

## Decision

### Packaging

- **`projects/ui` is an Angular library (`@6311/ui`) with an `ng-packagr` build target and a Vitest `test` target.**
  Its tsconfigs are `src/web/tsconfig.ui.json` and `tsconfig.ui.spec.json`, at the workspace root beside the app
  tsconfigs, and both are `strict` with `strictTemplates`. The workspace base isn't strict yet, so the library is
  the first strict code in the workspace.
- **Apps compile the library from source** through the `"@6311/ui"` path in `tsconfig.json`. There is no
  build-order dependency and no `dist/ui` in an app's resolution. `ng build ui` runs first in `npm run build`. It
  doesn't feed the apps; it is the full AOT, strict-template check of every primitive, including ones no app
  imports yet.
- **Styles are loaded through `angular.json`, not `@use`.** Each app lists `projects/ui/styles/global.scss` first
  in `styles`, and `global.scss` `@use`s `tokens.scss`. The tokens are CSS custom properties resolved at runtime,
  so app stylesheets need no Sass import to read them.

### Tokens

- **The spike's short names stay canonical** (`--ink`, `--text`, `--muted`, `--line`, `--accent`, `--hot`, …).
  They were already used 170 times. Renaming them to `--ui-color-*` would have changed every line of the
  site stylesheet without changing its output.
- **Every literal color became a token with exactly the same value.** Colors painted once get named for their
  surface (`--hero-from`, `--strip`, `--footer-from/to`, `--tier-rule`, `--tier-featured`, `--placeholder-*`). Translucent
  white and black are named by alpha (`--white-70`, `--black-58`), because their uses (veils, rules, on-dark text)
  don't share one meaning. The site stylesheet now has no literal colors. `art-image.ts` keeps its `hsl()`
  placeholder gradients, which are computed per artist and aren't part of the palette.
- **Type, space, border and motion tokens exist for the primitives and the admin.** The site's own font sizes and
  paddings weren't rewritten: the rule bans literal colors, and rewriting sizes would be churn with nothing to show.
- **`[data-density='compact']` tightens spacing for the admin.** Same type and palette, smaller `--space-*`,
  `--control-pad` and `--button-pad*`. The site never sets it.
- **Three accessibility corrections to the spike, approved by the site owner after design review (2026-10-20).**
  These are the only intended departures from the spike's rendering:
  - **Focus ring.** `--focus` defaults to `--ink`, at 16.6:1 on white. The spike's yellow was 1.05:1 on white
    and 1.11:1 on `--light`. Surfaces dark enough for ink to vanish set `--focus: var(--focus-on-dark)`, the
    yellow, in one site rule: `.nav`, `.hero`, `.band--dark`, `.band--photo`, `.tier--featured`, `.strip` and
    `.footer`. On those it measures at least 3.8:1. The ring changes only on focus, never at rest.
  - **Required marker.** The spike drew "required" in `--hot` (`#ff5a36`), which is 3.1:1 on white. It now uses
    `--hot-ink` (`#cf2500`): the same hue and saturation, darkened to 5.4:1 on white and 4.6:1 on `--light`.
    `--hot` stays for fills and rules.
  - **Stand-in portrait initials.** `--placeholder-ink` goes from `#9a9a9a` to `--muted` (`#666`). That's 3.7:1
    against the darkest end of the placeholder gradient, up from 1.8:1. The initials are `aria-hidden`, but they
    are still visible text.

### Primitives

| Primitive | Shape | Why that shape |
|---|---|---|
| `UiIcon` (`ui-icon`) | Component. `name: IconName` is a typed union | It replaces the site's `Icon` one-for-one. The host keeps `class="icon"`, so `.feature .icon` and similar still match. |
| `UiButton` (`button[uiButton]`, `a[uiButton]`) | Attribute directive applying the global `.btn*` classes | A wrapper component would break `type`, form submit, `routerLink` and native focus. `disabled` and `busy` are inputs so the directive owns the disabled state. |
| `UiField` + `UiControl` | Wrapper component plus a directive on the native control | The directive wires `id`, `aria-describedby`, `aria-invalid` and `aria-required` from the field. Control styling (`.field__control`) applies only through the directive, so the site's hand-written fields are untouched. |
| `UiNotice` | Component, tone `info / success / warning / error` | Errors use `role="alert"`, the others `role="status"`. Tone shows as glyph + rule color + a screen-reader word. It looks like the spike's `.sform__panel`. |
| `UiStatusChip` + `UiSyncStatus` | Generic chip (glyph required, word projected) plus a fixed sync-status wrapper | `design-system.md` rule 3. The sync words are fixed in `SYNC_STATUS`. Pending also uses a dashed rule, so tone is never only a hue. |
| `UiDialog` | Component on native `<dialog>` with `[(open)]` | Top layer, inert page and Escape come from the platform. `showModal()` runs in `afterRenderEffect`, so SSR renders it closed. Focus returns to the opener. Backdrop clicks are ignored unless asked for, so a stray click can't discard edits. |
| `UiEmptyState` | Component with projected text and `[uiEmptyActions]` | It takes the spike's `.empty` values. The heading level is an input so it fits either page outline. |

`.btn*` and `.field*` stay global because the site's templates write those classes by hand. The four new
primitives use their own BEM classes (`ui-notice`, `ui-status`, `ui-dialog`, `ui-empty`) with
`ViewEncapsulation.None`, so they can style projected content without `::ng-deep`.

### Alternatives considered

| Option | Cost |
|---|---|
| **Library from source via path mapping + ng-packagr check (chosen)** | Adds an `ng-packagr` devDependency and a second build step. |
| Path mapping only, no library build | Primitives no app imports yet are never compiled, so a broken template would surface in 2.2, not here. |
| npm workspace with `@6311/ui` as a linked package | A real package name for Sass too, at the cost of lockfile and `npm ci` changes for a library that is never published. |
| Namespaced tokens (`--ui-color-ink`) with aliases | Two names for every value, forever, for no change in output. |
| Moving the site's markup to `uiButton` / `ui-field` now | Puts the pixel-identical guarantee at risk, with no visible gain. The site adopts them when a page is next touched for a reason. |

## Consequences

- **The site renders pixel-identical.** Five routes (`/`, `/classes`, `/shop`, `/artists/submit`, `/memberships`) were
  captured at 1440×900 and 390×844, full page, with reduced motion emulated, from the pre-change and post-change
  SSR builds served side by side. pixelmatch at threshold 0 found 0 differing pixels in all ten pairs. A first
  pass with a 400 ms settle showed sub-perceptual noise (max channel delta 8) in the desktop home hero while its
  photo was still decoding. A before-against-before control at 1.5 s was also 0, so the full set was recaptured
  at 1.5 s.
- **It also matches the spike0 review copy** (`https://kind-pond-059a85510.4.azurestaticapps.net`, ADR 0014),
  which is the visual reference. Before the change, that copy's global stylesheet (`styles-CL4NNMYA.css`) was
  byte-identical to the site's, and git shows spike0's source was moved into `projects/site` unchanged. The same
  20 captures taken against the live copy are 0 differing pixels on nine of ten pairs. The tenth (mobile `/shop`)
  differs in 387 pixels: subpixel anti-aliasing of the native `<select>` text "Newest", which sits at a fractional
  y. Its geometry, computed font, padding and markup are identical, and the pre-change and post-change repo builds
  match each other there exactly.
- **The site's compiled CSS grows by about 3 kB** (new tokens, `var()` references, the field-control rules and the
  density block).
- **The three corrections were verified separately from the token extraction.** The extraction itself was proven
  pixel-identical first, as described above. After the corrections, resting-state captures of seven routes
  (adding `/about` and `/instructors`) differ from the pre-change build only in the "required" markers on
  `/artists/submit` and the initials on `/about` and `/instructors`. Home, classes, shop and memberships stay at
  0 differing pixels.
- **A keyboard audit checked every focus ring.** It tabbed through all 132 focus stops on the seven routes and
  measured each ring against the surface behind it. The spike build had 35 rings under 3:1 (1.05–1.11:1); this
  build has none. Text inputs and selects keep the spike's border-change indicator (light gray to ink), which
  the audit lists separately.
- **The site now intentionally differs from the spike0 review copy** in those three places, and in the focus
  ring on light surfaces.
- **The workspace base tsconfig still has no `strict` or `strictTemplates`,** although `frontend.md` requires
  both. The library opts in; turning it on for the site is its own prompt.
- **The dialog's unit tests run under jsdom, which has no `<dialog>`.** The spec installs `showModal`/`close`
  stand-ins and tests the component's contract (model, `closed`, focus return, backdrop gate). The top layer,
  inert page and Escape key are the browser's. They get exercised in a real browser when the admin first uses
  the dialog, or earlier if the `ui` test target moves to Vitest browser mode (`@vitest/browser-playwright`).
- **The `unit-test` builder needs `development` and `production` configurations on the library's build target.**
  They are empty because one tsconfig serves both. The library is never published, so there's no
  `compilationMode: partial`.
