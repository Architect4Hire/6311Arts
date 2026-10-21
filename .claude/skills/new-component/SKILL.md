---
name: new-component
description: Add an Angular 22 component to the 6311arts site (SSR), admin (CMS) or @6311/ui library with the right placement, states, accessibility and tests.
---

# Add an Angular Component

1. **Place it.**
   - Shared primitive used by both apps → `projects/ui/src/lib/<name>/`. Add a public export.
   - Site marketing composition → `projects/site/src/app/shared/` under the right kind (`layout/`, `cards/`,
     `forms/`, `media/`, or its own folder if it joins no group), or `pages/<page>/` for a page.
   - CMS screen → `projects/admin/src/app/features/<area>/<name>/`; a shared CMS control →
     `projects/admin/src/app/shared/` (`pickers/` if it is one).
   - Never add or move a tsconfig into a `projects/<app>/` folder. App tsconfigs stay at `src/web/tsconfig.<project>.json` (`frontend.md`, Workspace config).
2. **Search `@6311/ui` and the existing components first.** Compose before you create.
3. **Define before writing:**
   - selector
   - typed signal inputs and outputs
   - projected content
   - ARIA behaviour
   - responsive behaviour
   - every state: loading, empty, error, success, disabled
4. **Give it its own folder, named after it** (ADR 0050, 0051, `frontend.md`):

   ```text
   <name>/<name>.ts        the decorator + class: templateUrl: './<name>.html', styleUrl: './<name>.scss'
   <name>/<name>.html      the markup
   <name>/<name>.scss      the styles — omit the file entirely if the component has none
   <name>/<name>.spec.ts   the spec, when it covers this component alone
   ```

   - **One `@Component` per file.** Never declare two in one file, and never add a barrel re-exporting a folder
     of components: an importer that wants one would pull its siblings into the same lazy chunk.
   - Non-component exports stay with the component that owns them (`ShowMoment` with `ShowCard`).
   - Never write `template:` or `styles:` in a `@Component`. The only exception is a throwaway host component
     inside a `*.spec.ts`, whose inline `template:` is that test's fixture.
   - Don't add a second level to a folder already named after the component (`features/dashboard/dashboard.ts`),
     and don't create a category folder for a single component.
   - The stylesheet is `.scss`, never `.css`: component styles here are SCSS (nested selectors, `//` comments)
     and a `styleUrl` is compiled according to its extension.
   - Run Prettier over the new `.html`; `src/web/.prettierrc` maps it to the `angular` parser.
5. **Build it:** standalone, `OnPush`, strict templates, semantic HTML, accessible names, tokens only (no literal colors).
6. **Data** comes from typed services and models. Never inject `HttpClient`.
7. **SSR (site).**
   - No `window`/`document` access outside `afterNextRender` / `isPlatformBrowser`.
   - Data fetched during SSR goes through the transfer cache.
   - SEO set through `SeoService`.
   - Content is never hidden from crawlers.
8. **Admin.** Square-held values (price, quantity, seats) are never edited in place. They show the current figure with a **Change** command and its pending state, plus the sync chip (ADR 0032). Unsaved edits warn on navigation. A 409 shows a conflict view.
9. **Test** behaviour and accessibility with Vitest. Run `npm run build` and `npm test` from `src/web/`. If the `test` target doesn't exist yet, say so instead of claiming tests pass (`frontend.md`, Verification). Then run `@design-review`.

## Checklist before done

- [ ] Right project and folder; reuses `@6311/ui`
- [ ] Own folder named after the component; one `@Component` per file; no barrel
- [ ] Markup in a sibling `.html`, styles in a sibling `.scss`; no `template:`/`styles:` in the decorator
- [ ] All states implemented; keyboard, focus, 200% zoom, reduced motion verified
- [ ] SSR-safe (site) / conflict- and sync-aware (admin)
- [ ] Tests pass; design review run
