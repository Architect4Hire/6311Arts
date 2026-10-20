---
name: new-component
description: Add an Angular 22 component to the 6311arts site (SSR), admin (CMS) or @6311/ui library with the right placement, states, accessibility and tests.
---

# Add an Angular Component

1. **Place it.**
   - Shared primitive used by both apps → `projects/ui`. Add a public export.
   - Site marketing composition → `projects/site/src/app/shared/` or a page.
   - CMS screen → `projects/admin/src/app/features/<area>/`.
   - Never add or move a tsconfig into a `projects/<app>/` folder. App tsconfigs stay at `src/web/tsconfig.<project>.json` (`frontend.md`, Workspace config).
2. **Search `@6311/ui` and the existing components first.** Compose before you create.
3. **Define before writing:**
   - selector
   - typed signal inputs and outputs
   - projected content
   - ARIA behaviour
   - responsive behaviour
   - every state: loading, empty, error, success, disabled
4. **Build it:** standalone, `OnPush`, strict templates, semantic HTML, accessible names, tokens only (no literal colors).
5. **Data** comes from typed services and models. Never inject `HttpClient`.
6. **SSR (site).**
   - No `window`/`document` access outside `afterNextRender` / `isPlatformBrowser`.
   - Data fetched during SSR goes through the transfer cache.
   - SEO set through `SeoService`.
   - Content is never hidden from crawlers.
7. **Admin.** Square-held values (price, quantity, seats) are never edited in place. They show the current figure with a **Change** command and its pending state, plus the sync chip (ADR 0032). Unsaved edits warn on navigation. A 409 shows a conflict view.
8. **Test** behaviour and accessibility with Vitest. Run `npm run build` and `npm test` from `src/web/`. If the `test` target doesn't exist yet, say so instead of claiming tests pass (`frontend.md`, Verification). Then run `@design-review`.

## Checklist before done

- [ ] Right project and folder; reuses `@6311/ui`
- [ ] All states implemented; keyboard, focus, 200% zoom, reduced motion verified
- [ ] SSR-safe (site) / conflict- and sync-aware (admin)
- [ ] Tests pass; design review run
