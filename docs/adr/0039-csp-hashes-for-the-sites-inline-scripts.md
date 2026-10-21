# 0039 — The site's inline scripts are allowed by hash, not by nonce

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/gateway.md`, `.claude/rules/frontend.md`
- **Builds on:** [0001](0001-single-public-origin-behind-a-yarp-gateway.md) (one origin, so one place sets CSP),
  [0027](0027-angular-workspace-ssr-and-the-cms-shell.md) (SSR, hydration with event replay)

## Context

Walking the site through the gateway in a real browser showed the console refusing **three inline scripts** on
every page:

```text
Refused to execute inline script because it violates the following
Content-Security-Policy directive: "script-src 'self'"
```

They are not ours to delete:

| Script | Where it comes from | Bytes |
|---|---|---|
| `document.documentElement.classList.add('js')` | the site's `index.html`, the spike's progressive-enhancement hook | 44 |
| Angular's event-dispatch contract | `@angular/core`, emitted during SSR | 476 |
| `window.__jsaction_bootstrap(document.body, "ng", [...])` | `withEventReplay()`, arms replay | 120 |

The failure is **silent**. Hydration still completes (we measured 46 components, 0 skipped), the page looks
right, and nothing surfaces to a user or a log — but every click, submit or keystroke made before hydration is
dropped on the floor, which is the one thing event replay exists to prevent. `frontend.md` keeps event replay
from the spike deliberately, so leaving it refused is not an option.

CSP offers two ways to allow an inline script: a per-response **nonce**, or a **hash** of its content.

## Decision

**Allow all three by `sha256-` hash in `Gateway:Csp:Default`, and never by nonce or `'unsafe-inline'`.**

The gateway keeps sole ownership of the header (ADR 0001); nothing is injected into the SSR body.

```text
script-src 'self'
  'sha256-Du+OJKJSbdUgz5nrHeWWINvez6XKDDU/tyj/5c2uvwo='   the js class
  'sha256-VM2mZqyEQZoLzoTrp5EigFvzQ0+f1wSeBuoOn95WHCg='   Angular event dispatch
  'sha256-WAFInstuijDmkdZL9gTCFeDmZQw9HbGd+IA1OUdNqjo='   __jsaction_bootstrap
```

### The hazard this creates, and the test that answers it

The third script's text embeds **the event types the site's own templates bind**
(`["click","submit","input","compositionstart","compositionend"],["blur"]`). Add a `(change)` or `(keydown)`
binding anywhere in `projects/site` and that hash changes — and event replay dies again, silently, exactly as it
did before.

A literal hash in a test would be worthless here: it would need the same edit. So
`EveryInlineScriptOnTheRenderedSiteIsAllowedByItsOwnCsp` (`Category=E2E`) fetches the rendered page through the
gateway, extracts every executable inline script, hashes each one, and asserts the response's **own** CSP header
allows it — naming the missing hash in the failure message. The drift is caught by the suite rather than by a
visitor losing a click.

### Why not a nonce

- **Cached HTML and per-response nonces are incompatible.** SSR pages are served
  `public, max-age=0, s-maxage=300, stale-while-revalidate=86400`. A shared cache keeps one body — carrying
  nonce A — and pairs it with a later response's header carrying nonce B. Every script is then refused, on a
  schedule nobody can reproduce locally.
- **Angular's own support is broken for this case.** With `withEventReplay()`, only one of the two scripts
  receives the nonce from `CSP_NONCE`
  ([angular/angular#59886](https://github.com/angular/angular/issues/59886)).
- **The gateway would have to rewrite the body.** A nonce must appear in the header *and* in the HTML; the
  gateway proxies the site's SSR response and has no business parsing or mutating it.

### WordPress origins, for the migration only

The site still reads classes, exhibitions and shop items from the live WordPress (`WpApi`), and renders images
from Jetpack's CDN. The shipped policy refuses both, so locally the page fell back to bundled sample data with
broken images. `appsettings.Development.json` adds exactly two sources, through a new additive key:

```json
"Csp": { "AdditionalSources": { "img-src": "https://i0.wp.com", "connect-src": "https://6311arts.com" } }
```

`CspOptions.AdditionalSources` appends to whichever policy a path resolved to, and only to a directive that
policy already names. **An environment must never restate `Default`,** which is what this key exists to prevent:
written first as a plain `Default` override, the development file held its own copy of the three hashes — and
when one was deliberately staled to check that the drift test worked, the test still passed, because the
development copy was the one being served. A second copy of a policy is a second place for a hash to rot, in the
half nobody is looking at.

**This is a crutch with an expiry date.** It is development-only, and `TheWordPressOriginsAreDevelopmentOnly`
asserts both halves — present in Development, absent from the shipped policy — because a crutch nobody tests is
a crutch that ships. It comes out when those pages read from `/api/v1/site/**`.

## Options considered

| Option | Cost |
|---|---|
| **Hashes (chosen)** | A template change can invalidate one hash — answered by the E2E drift test |
| Per-response nonce | Breaks under `s-maxage` caching; Angular #59886; gateway must rewrite the body |
| `'unsafe-inline'` | Abandons the protection the directive exists for, for three known scripts |
| Drop `withEventReplay()` | Loses pre-hydration clicks; contradicts `frontend.md` |
| Allow the WordPress origins everywhere | Puts a migration crutch in the deployed policy |

## Consequences

- Event replay works through the gateway, which is the only way the site is ever browsed.
- `script-src` carries no `'unsafe-inline'` and no `'unsafe-eval'`; a test asserts both.
- An Angular upgrade that changes either framework script fails the E2E test with the hash to paste in.
- A new inline script anywhere in the site is refused until its hash is added — deliberately. Prefer a bundled
  module.
- The deployed policy stays free of WordPress origins, and the migration has one named place to clean up.
