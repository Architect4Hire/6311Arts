# 6311arts — Site Analytics (GA4) SCRUB Prompts

Atomic prompts that add **one analytics seam to the public SSR site** and nothing to Studio: a provider-neutral
`Analytics` service that any page or component can call, a Google Analytics 4 sink behind it, automatic page-view
tracking across SPA navigations, and a typed catalogue of the events 6311arts actually wants to count.

The architectural source of truth is `CLAUDE.md` plus `.claude/rules/frontend.md`, `gateway.md` and
`design-system.md`. Two existing decisions constrain almost every prompt here, so read them before the first one:

- **ADR 0039 — the site's CSP carries `sha256-` hashes and no `'unsafe-inline'`, and never a nonce.** SSR pages are
  cached with `s-maxage`, so a per-response nonce and the cached body drift apart. **A new inline script is refused
  by design — bundle it instead.** That is why the tag is injected by TypeScript rather than pasted into
  `index.html`, and why no prompt here adds a `<script>` to the document.
- **ADR 0029 — emailed-link credentials arrive in `location.hash`.** Anything that reads `location.href` can send
  a password-reset token to Google. No code in this series reads `location.href`, ever.

**Rules:** `frontend.md` (the site's services, SSR rules, "no HttpClient in a component"), `gateway.md` (the CSP and
its tests), `design-system.md` (the one component this series adds)
**Skills:** `new-component`
**Reviewers:** `@security-reviewer`, `@design-review`, `@architecture-reviewer`, `@test-gap-analyzer`

## Decisions taken before this file was written

| Decision | Settled as |
|---|---|
| How the tag loads | **`gtag.js`, injected at runtime by the service.** `dataLayer` and the `gtag` shim are TypeScript. No inline script, so the CSP stays hash-only and the E2E hash test keeps passing. |
| Consent | **No banner. Standard GA4 cookies, disclosed on the privacy page.** Appropriate for a Chicago nonprofit's largely local audience; the trade-off and the switch that would change it are recorded in G.1. |
| What is captured | Page navigation, content views, forms and newsletter, intent clicks — and **GA4 ecommerce events as a later prompt** (G.8), because they need Phase 5's cart and checkout. |
| Where it sends | **A separate GA4 property for development**, so the wiring can be proved in Google's own reports before launch. The measurement id is per-environment configuration; with none set the service is inert. |
| Studio | **Untouched.** No analytics code in `projects/admin`, and a test asserts the admin bundle contains none. |

**What "no banner" does and doesn't buy.** Standard GA4 cookies with a privacy-page disclosure is the common US
posture and is what this series builds. It is *not* a defensible position for EU/UK visitors, who need consent
before analytics storage. G.1 therefore keeps one seam — a `ConsentState` the sink reads before its first
`gtag('config')` — hard-coded to "granted" with a remark, so adding Consent Mode v2 later is a banner and a
provider, not a rewrite of the service. Google Signals and ad personalization stay **off** (G.7): switching them on
is what turns analytics into advertising data and would change the legal picture.

## Where it slots into the run order

```text
3.2 + 3.3 (the site on the platform) ──> G.1 … G.7 ──> G.9 ✅
5.13 + 5.14 (cart and checkout) ──────────────────────> G.8 ──> G.9 ✅
```

| Prompts | Need | Notes |
|---|---|---|
| G.1 | nothing | The ADR. Decide before any code. |
| G.2 | 3.3 (`server.ts` on the platform) | Configuration and the CSP. Nothing is measured yet. |
| G.3 – G.4 | G.2 | The seam, the sink, and page navigation. This is the useful minimum: ship it and stop if you like. |
| G.5 – G.6 | G.4 | The events, snapped into the existing pages. |
| G.7 | G.4 | The privacy disclosure, the property settings, the runbook. **Do not go live without it.** |
| G.8 | G.5, **5.13 + 5.14** | Ecommerce. Skip it entirely until the cart exists. |
| G.9 | everything shipped | Checkpoint. |

## The seam

```text
page / component ──> Analytics (core/analytics/, provider-neutral, typed events)
                        └──> GoogleAnalyticsSink ──> window.dataLayer ──> gtag.js
Router NavigationEnd ──> Analytics.pageView(redacted path, title)      (wired once, in provideAnalytics())
[appTrack] directive ──> Analytics.track(event)                        (snapped onto any element)
```

- **One call site shape.** A page calls `analytics.track(AnalyticsEvents.viewContent({ type: 'shop-item', slug }))`.
  It never names `gtag`, `dataLayer` or Google, and never injects `HttpClient` (frontend.md).
- **"Snapped into every page" is one provider, not one import per page.** `provideAnalytics()` in
  `app.config.ts` subscribes to the router once, so every current and future route is counted with no per-page
  work. Only the extra events are per-page, and only where a page has something worth counting.
- **The sink is replaceable.** `Analytics` is the interface the app depends on; `GoogleAnalyticsSink` is one
  implementation, and a no-op sink is what runs on the server and when no measurement id is configured.

## What may never be sent

This list is the series' spine. Every prompt restates the part that applies to it, and G.9 tests all of it.

| Never | Why |
|---|---|
| `location.href`, `location.search`, `location.hash` | A reset or invite token arrives in the fragment (ADR 0029), and `captureLinkFragment` clearing it is a race this code must not depend on. The path comes from the **Router**, and query strings are dropped. |
| A `/preview/{token}` path as it stands | ADR 0036's preview token is in the path. It is redacted to `/preview/(token)`, mirroring `ServiceDefaults.RedactSecretSegments`. |
| Any `/portal/**` page | The artist portal is signed-in, `no-store` and nobody's business (artist-accounts series, ADR 0055). |
| An email address, name, user id, `user_id`, member or artist status, or a form's answers | The analytics property is not a place people's data goes. Only the *fact* of a submission and which form. |
| Money in cents | GA4 expects decimal currency units. Cents reach it divided, or every report is wrong by 100× (G.8). |
| A new inline `<script>` | ADR 0039. Bundle it. |
| Anything at all from `projects/admin` | Studio is staff tooling, not a measured surface. |

---

## G.1 — The ADR

```
SCOPE: Write docs/adr/0059-site-analytics.md (claim 0059; the artist-accounts series claims 0055–0058, so
renumber if either lands differently), one ADR with context, decision, options considered and consequences:
gtag.js injected by an Angular service rather than pasted into index.html (ADR 0039: no inline script, no
nonce); a provider-neutral Analytics seam with a Google sink behind it; manual page_view on router
navigation with send_page_view false; standard GA4 cookies with a privacy-page disclosure and no banner,
with the ConsentState seam left in place and the EU/UK exposure written down plainly; a separate GA4
property per environment, inert with no measurement id; the redaction and never-send list above; Google
Signals and ad personalization off; the site only, never Studio.

CONSTRAINT: docs/adr/README.md format. Cite ADR 0029 (fragment credentials), 0036 (preview tokens), 0039
(CSP hashes), 0046 (server config never bundled) and frontend.md.

RESTRICTION: Do NOT write code, configuration or privacy copy here. Do NOT decide the DECIDE items at the
end of this file — record the options and their costs.

UTILIZATION: Read ADR 0039 and 0046 first; frontend.md.

BEHAVIOR: Draft it, wait for my review, then commit.
```

## G.2 — Configuration and the CSP

```
SCOPE: Get a measurement id to the browser and let the browser reach Google.
AppHost: a site-ga-measurement-id parameter (developer-supplied, user-secrets, empty allowed) passed to the
site resource as SITE_GA_MEASUREMENT_ID beside SITE_API_ORIGIN / NG_ALLOWED_HOSTS / SITE_MOCK_FALLBACK.
Site: app.config.server.ts reads process.env['SITE_GA_MEASUREMENT_ID'] once per render and puts it in
TransferState under a GA_MEASUREMENT_ID key, exactly as MOCK_FALLBACK_KEY already does; a browser-side
provider reads it back, and an ANALYTICS_CONFIG injection token in core/analytics/ carries
{ measurementId: string | null, debug: boolean }. Nothing is bundled at build time, and nothing is written
into the HTML as script.
Gateway: Gateway:Csp:Default gains script-src https://www.googletagmanager.com, connect-src
https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com, and
img-src https://*.google-analytics.com. They go in the SHIPPED Default policy, not in an environment's
AdditionalSources: the origins are needed in production, and ADR 0039 forbids an environment restating
Default. Add a preconnect for www.googletagmanager.com to the site's index.html.

CONSTRAINT: gateway.md (the CSP keys and that a policy is one string); frontend.md ("SSR calls … server-side
config, never bundled"); ADR 0039, 0046; CLAUDE.md "no hardcoded … tokens or keys".

RESTRICTION: No measurement id in angular.json, an environment.ts, a committed file or the bundle. No
'unsafe-inline', no nonce, no new inline script, and no change to the three existing script-src hashes. Do
NOT add the Google origins to the admin app's surface or to any CSP override. An empty id must leave the
site behaving exactly as it does today.

UTILIZATION: add-aspire-resource (the parameter), @security-reviewer.

BEHAVIOR: Plan the parameter, the env var, the TransferState hand-off and the CSP diff; wait; implement;
tests: the shipped Default names each Google origin and still names the three hashes, an environment adds
none of them, the id reaches a browser provider through TransferState and is null when unset, the rendered
HTML contains no Google script and no measurement id in executable script, and the Category=E2E CSP hash
test still passes. Report the CSP string before and after.
```

## G.3 — The analytics seam and the Google sink

```
SCOPE: core/analytics/: the Analytics interface the whole site depends on (pageView(path, title),
track(event), and nothing else), a typed AnalyticsEvent union with a small AnalyticsEvents factory so event
names and parameter shapes are declared in one place, a NoopAnalytics used on the server and whenever no
measurement id is configured, and GoogleAnalyticsSink — which on first use defines window.dataLayer and the
gtag shim in TypeScript, appends the googletagmanager.com script with async and a load-failure handler that
swallows the error, and calls gtag('js') and gtag('config', id, { send_page_view: false,
allow_google_signals: false, allow_ad_personalization_signals: false }). A ConsentState provider returns
'granted' today and is the single place a banner would later change (G.1). Events queue in dataLayer before
the script arrives, so nothing is lost and nothing waits. provideAnalytics() wires it in app.config.ts and
picks the sink from ANALYTICS_CONFIG and the platform. With debug true the sink logs each event instead of
sending it.

CONSTRAINT: frontend.md (typed services own all transport, components never do; signals for state; strict
TypeScript, never any; three files never one, one component per folder — ADR 0050, 0051); ADR 0059.

RESTRICTION: Nothing runs on the server: no window, document, navigator or script tag during SSR, and the
rendered HTML must be byte-identical to today's. An ad blocker or a failed script load must never throw,
never retry in a loop and never show the visitor anything. No page or component may import the sink, name
gtag or touch dataLayer. No analytics call in projects/admin or projects/ui.

UTILIZATION: @architecture-reviewer, @security-reviewer.

BEHAVIOR: Plan the interface, the event union and the sink's lifecycle; wait; implement; npm test with a
fake dataLayer; tests: the server sink does nothing and touches no global, a null id yields the no-op sink,
the script is appended exactly once for many events, events sent before the script loads survive in
dataLayer, a load failure is swallowed, config sets send_page_view false and both Google-signals flags
false, and no network call happens in a unit test. Report.
```

## G.4 — Page navigation

```
SCOPE: Automatic page views across SSR and SPA navigation: provideAnalytics() subscribes to the Router's
NavigationEnd once, sends exactly one page_view for the first page after hydration and one per later
navigation, and derives page_location from the site origin plus the Router's URL with the query string and
fragment dropped, page_path likewise, and page_title from what SeoService has set (read after navigation, so
the title is the new page's). Redaction and exclusion live here, in one pure function with its own spec:
/preview/{token} becomes /preview/(token), /portal/** is not reported at all, and the function is the only
place a path is turned into an analytics value. A navigation that only changes the fragment sends nothing.

CONSTRAINT: frontend.md; ADR 0029 (never location.href), 0036 (preview tokens), 0059. Hydration with event
replay and incremental hydration is on (app.config.ts), so the first view must be sent once, not once per
hydrated block.

RESTRICTION: Never read location.href, location.search or location.hash — the Router is the only source.
Never send a query string, a fragment or an unredacted preview path. Never a user id or any visitor
identifier beyond what GA4 sets for itself. Prerendered pages (about, visit, submit, artists/submit) must
stay prerendered and must not gain a tag in their built HTML.

UTILIZATION: @security-reviewer, @test-gap-analyzer.

BEHAVIOR: Plan the subscription, the title timing and the redaction table; wait; implement; npm test; tests:
one page_view per navigation and exactly one on first load, a fragment-only navigation sends nothing, a
reset-link URL with a token fragment sends a path with no token (prove it from a URL that has one), a
preview path is redacted, a portal path sends nothing, the title is the destination's; then walk me through
the site with debug on and show me the logged sequence for home → shop → an item → back.
```

## G.5 — Content views and intent clicks

```
SCOPE: The two event families that need page cooperation.
Content views: each detail page sends one view_content with { contentType, slug } on load — artist-detail,
class-detail, instructor-detail, product-detail, exhibitions, cms-page, memberships — through the typed
factory, never a hand-written event name.
Intent clicks: an [appTrack] attribute directive at the root of shared/ (beside format.ts and reveal.ts, per
frontend.md and ADR 0051), taking a typed event and sending it on click or keyboard activation, snapped onto
the "Buy" and "Enroll" affordances, membership tier calls to action, the map, phone and email links on
/visit, and outbound links in the footer. The directive never prevents, delays or alters the navigation it
measures.

CONSTRAINT: frontend.md (directives at the root of shared/; one component per folder; no HttpClient in a
component); design-system.md; ADR 0051, 0059.

RESTRICTION: No new event name outside the factory. No visitor identifier, no price, no stock figure and no
Square-owned value in an event — a slug and a content type are the whole payload. The directive must not
change focus order, keyboard behaviour or markup semantics, and a tracked link must still work with
analytics blocked or the sink no-op. No change to any page's layout or copy.

UTILIZATION: new-component; @design-review, @security-reviewer, @architecture-reviewer.

BEHAVIOR: Plan the event catalogue additions, the directive's API and every element it is snapped onto;
wait; implement; npm test; tests: one view_content per detail page with the right type and slug, the
directive fires once per activation for mouse and keyboard, navigation still happens when the sink throws,
and no event carries anything outside its declared shape. Then walk me through the list with debug on.
```

## G.6 — Forms and newsletter

```
SCOPE: Events for the Forms surfaces (ADR 0047): a newsletter signup, a contact message sent, and the artist
submission started and sent — the fact, which form, and whether it succeeded or was refused, sent after the
FormsApi call resolves so a failed post is not counted as a lead. Use GA4's generate_lead / sign_up names
where they fit, through the factory.

CONSTRAINT: frontend.md (forms post through FormsApi with the antiforgery header); ADR 0047, 0059.

RESTRICTION: Never an address, a name, a message body, an answer, an uploaded file's name or a validation
message. Never an event before the response. A refused or rate-limited submission is one counted outcome,
not a lead. No change to what any form sends to the API.

UTILIZATION: @security-reviewer, @test-gap-analyzer.

BEHAVIOR: Plan the four events and where each fires; wait; implement; npm test; tests: a successful post
sends one event and a failed one sends the refusal outcome, no event payload contains any field the visitor
typed (assert on the serialized payload, not by eye), and the form still works with the sink throwing.
```

## G.7 — The privacy disclosure, the property settings, the runbook

```
SCOPE: What makes "no banner" honest rather than merely quiet.
Draft the privacy-page copy for staff to paste into the CMS Page record (the CMS owns the text; this prompt
produces a reviewed draft in docs/runbooks/analytics.md, not a seeded page): what is collected, that Google
Analytics sets cookies, what is never collected, how a visitor opts out (browser controls and Google's own
opt-out add-on), and who to write to. Then docs/runbooks/analytics.md itself: how to create the two
properties, where the id goes (AppHost user-secrets locally, the publish parameter in Azure), the property
settings that must be set by hand — Google Signals OFF, ad personalization OFF, data retention set
deliberately, internal traffic and the gallery's own IPs filtered, cross-domain nothing — how to verify with
debug mode, and how to turn analytics off in one step (clear the id, redeploy).

CONSTRAINT: content.md (the privacy page is a CMS Page; nothing here seeds or edits content); ADR 0059.

RESTRICTION: Do NOT write the copy into the site bundle, a component, a seed or a migration. Do NOT claim
consent is not required for EU/UK visitors — say what the posture is and what would change it. No legal
advice: the draft is for a person to approve.

UTILIZATION: Read ADR 0059; docs/runbooks/README.md.

BEHAVIOR: Draft the copy and the runbook, wait for my review, then commit. Flag anything you think a lawyer
should see.
```

## G.8 — Ecommerce events (after Phase 5)

```
SCOPE: GA4 ecommerce for the shop and classes, once 5.13 (cart and quote) and 5.14 (checkout pay) exist:
view_item, add_to_cart, begin_checkout and purchase, built through the typed factory with items carrying
item_id (the CMS SKU), item_name, item_category and quantity, value in DECIMAL CURRENCY UNITS converted
from the domain's long cents, and currency 'USD'. purchase carries transaction_id (the order id) and fires
once for an order: the confirmation page sends it on first render and records the order id so a reload does
not count a second purchase.

CONSTRAINT: square.md and backend.md (money is long cents in the domain; the conversion happens at this
boundary and nowhere else); frontend.md ("Square-held values are never edited in place" — here they are not
even sent except as the figure the visitor was charged); ADR 0059 plus its ecommerce section, which this
prompt extends or supersedes with its own ADR if the shape changed.

RESTRICTION: Never a card detail, a payment token, a source id, a customer name, an address or an email —
not in an event, not in a parameter, not in a debug log. Never a cents figure sent as if it were dollars.
Never an amount taken from the client: the figures come from the quote and the order the API returned. A
duplicate purchase event is a reporting defect, so the dedupe is tested.

UTILIZATION: @security-reviewer, @integration-reviewer, @test-gap-analyzer.

BEHAVIOR: Plan the four events, the cents conversion and the purchase dedupe; wait; implement; npm test;
tests: 4250 cents becomes 42.50 exactly (and a rounding case), a reload of the confirmation page sends no
second purchase, no event payload contains any payment or customer field, and the sink being blocked never
affects checkout. Report.
```

## G.9 — ✅ Checkpoint

```
SCOPE: Close the series. Run @security-reviewer, @design-review, @architecture-reviewer and
@test-gap-analyzer over everything G.2–G.8 touched, and fix what they find. Add the Category=E2E test that
proves the invariants from one rendered page: the page's inline scripts are still all CSP-allowed, the
rendered HTML carries no measurement id in executable script, a reset-link URL carrying a token fragment
produces an analytics path with no token, and the admin bundle contains no analytics code and no Google
origin. Update .claude/rules/frontend.md with one Analytics section (the seam, the one provider, the
never-send list, that Studio is out of scope) and gateway.md's CSP notes, and add a row to
docs/prompts/scrub-prompts.md's phase map pointing here.

CONSTRAINT: CLAUDE.md "Usage" (npm run build and npm test from src/web; dotnet test from the root for the
gateway and E2E tests; E2E needs Docker).

RESTRICTION: Do NOT weaken a redaction, an exclusion or the CSP to make a test pass — bring it back here
instead. No rule edit that changes behaviour: the rules follow what the series already agreed.

UTILIZATION: every reviewer above; playwright-cli for the walk-through.

BEHAVIOR: Run the reviewers, show me their findings and your fix list, wait; then fix, write the E2E test,
edit the rules, run the full frontend and backend suites, and report: what each reviewer said, what changed,
what the dev property shows for a manual walk of the site, and anything still open.
```

---

## What the series owes the rule files

G.9 makes these edits; every prompt should be written knowing they're coming.

| File | Edit |
|---|---|
| `frontend.md` | one **Analytics** section: the `Analytics` seam and that pages never name `gtag`; `provideAnalytics()` as the single wiring point; the never-send list; no analytics in `projects/admin` or `projects/ui`; the sink is inert on the server and with no id |
| `gateway.md` | the Google origins in the shipped `Default` CSP, why they are not in `AdditionalSources` (ADR 0039), and the test that asserts both halves |

## Open decisions (`DECIDE` in G.1)

- **Whether to honour Global Privacy Control and `Do Not Track`.** Cheap to add (`navigator.globalPrivacyControl`
  → the `ConsentState` seam already in G.3 → never initialize the sink), and a reasonable thing for a nonprofit to
  do voluntarily given there is no banner. It costs some traffic data. Not built unless you say so.
- **Whether `/signin` and `/account/**` are counted at all.** They are harmless once query strings and fragments
  are dropped, and a reset-page view count is genuinely useful for support. The series counts them; excluding them
  is a one-line change to G.4's table.
- **Where the purchase dedupe marker lives** (G.8). `sessionStorage` keyed by order id is the obvious answer and is
  not session or personal data, but frontend.md's "no session data in browser storage" is close enough that it
  deserves a sentence in the ADR rather than a quiet exception.
- **Whether a second sink is ever wanted** (a self-hosted Plausible or Umami alongside or instead of GA4). The seam
  makes it possible; nothing here assumes it.
- **Whether staff want in-CMS reporting.** Out of bounds by CLAUDE.md ("no order/customer reporting in the CMS")
  and out of scope here: GA4's own console is the reporting surface.

## Verification, per CLAUDE.md

```text
cd src/web && npm ci && npm run build && npm test
dotnet build -warnaserror && dotnet test        # gateway CSP tests and the E2E invariants; E2E needs Docker
aspire run                                      # browse only through the gateway endpoint
```

With `SITE_GA_MEASUREMENT_ID` unset the site must behave exactly as it does today, build identically, and send
nothing. That is the first thing to check after every prompt in this series.
