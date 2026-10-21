---
paths:
  - src/web/projects/ui/**
  - src/web/projects/site/src/**/*.scss
  - src/web/projects/admin/src/**/*.scss
---
# 6311arts Design System

The visual source of truth is the site the spike established. It's styled after the CSoon template:

- a black-and-white photo hero
- charcoal and pale-gray bands
- uppercase, letter-spaced type
- square outlined buttons
- thin line icons
- Open Sans

There's no Bootstrap or jQuery.

## Where things live

- **Tokens** (`projects/ui/styles/tokens.scss`): color, type scale, spacing, borders, motion. They're extracted from the spike's `styles.scss`. Feature styles use tokens and never literal colors.
- **Global styles** (`projects/ui/styles/global.scss`): imported once per app.
- **Component styles**: a sibling `.scss` inside the component's own folder, named in `styleUrl` — never a
  `styles:` block in the decorator (ADR 0050, 0051, `frontend.md`).
- **Shared primitives** (`@6311/ui`): only what both apps use — button, icon, field, notice, status chip, sync status, dialog, empty state; one folder per component under `lib/`. The site's marketing compositions (hero, bands, cards) stay in `projects/site/src/app/shared/`, grouped by kind (`layout/`, `cards/`, `forms/`, `media/`).
- **Admin** uses the same tokens at productivity density. Same typography and palette, tighter spacing.

## Rules

1. **Search first.** Look through the `@6311/ui` public API before adding a component. Prefer composition over new variants.
2. **Import publicly.** Never deep-import library internals.
3. **Status is never color alone.** It needs a glyph or word too (sync status: Synced / Pending / Needs attention; change requests: Pending / Applied / Rejected).
4. **Check the license.** The CSoon template is a commercial WrapBootstrap theme, so its license must cover this use. Record that in an ADR before launch.
5. **Don't commit build output.** `dist/` and `node_modules/` are never committed.
6. **Match spike0 to the pixel.** The live review copy (ADR 0014) is the reference. Screenshot-diff any style change against it. The only approved departures are the three accessibility corrections in ADR 0025.
7. **Focus rings use `--focus`, never a literal color.** It is ink by default. A new dark, photo or gray surface joins the `--focus: var(--focus-on-dark)` rule at the top of the site's `styles.scss`.
8. **`--hot` is for fills and rules only.** Orange text uses `--hot-ink`.
