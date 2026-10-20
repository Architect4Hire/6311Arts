# 0029 — Emailed-link pages on the site, and asking for a new confirmation link

- **Status:** Accepted
- **Date:** 2026-10-20
- **Rules affected:** `.claude/rules/auth.md`, `.claude/rules/gateway.md`, `.claude/rules/outbox.md`,
  `.claude/rules/frontend.md`
- **Builds on:** [0027](0027-admin-shell-and-staff-sign-in-page.md) (the sign-in page's patterns),
  [0028](0028-password-reset-and-email-confirmation-routes.md) (the routes these pages call). **Amends 0028**: its
  hand-off queue now carries confirmation requests too, under a renamed configuration section.

## Context

Since 1.9 the reset and confirmation emails have linked to `/account/reset-password#userId=…&token=…` and
`/account/confirm-email#…`, and 2.2a built the gateway routes those links need. Prompt 2.2b adds the pages. The forces:

- **The token is a credential for an hour.** It must never reach a server (a URL, the SSR render, a log), browser
  storage or the console. Browsers never send a fragment, so the server cannot see it; what remains is everything
  inside the browser that reads the URL: the router, anchor scrolling, history.
- **"Failure offers a fresh request."** For a reset that is `/bff/forgot-password`. For a confirmation there was
  nothing: the only way to send a confirmation email was `RequestEmailConfirmationAsync(userId)`, which needs a user
  id the person no longer has once a link has failed. An unconfirmed Editor whose link expired could not sign in and
  could not fix it.
- Asking for an email by address is the reset request's timing oracle again (0028), plus a second one: whether the
  address is already confirmed.

## Decision

### The fragment is taken before Angular starts

`main.ts` calls `captureLinkFragment()` before `bootstrapApplication`. On the two link paths, and only there, it
reads `userId` and `token`, calls `history.replaceState` to drop the fragment (keeping the path and query), and
holds the pair in a module variable. `LinkFragment.take()` hands it to the page once.

- **Before bootstrap, not in a component.** By the time any component runs, the router has read the URL and holds
  the token as the route's fragment, and `anchorScrolling` has searched the page for an element named after it.
- **Parsed by hand, not with `URLSearchParams`**, which reads `+` as a space; Identity's tokens are Base64.
- **A malformed fragment is cleared as surely as a good one**, and anything longer than the gateway's ceilings is
  not a link we sent.
- **The cost:** a refresh loses the link, since it is held nowhere but memory. The page says the link cannot be
  used and offers a new one in place.

The pages keep the credentials in a private field, never a signal the template reads, and drop them once spent or
refused. Tests assert the token is in neither the DOM nor any console call.

### The pages

- `/account/forgot-password`, `/account/reset-password` and `/account/confirm-email`: server-rendered, `noindex`,
  `Cache-Control: no-store` (through a shared `noStore()`, as `/signin` does), forms disabled until hydration and
  carrying `method="post"` (0027's reasons).
- **Confirmation posts as soon as the page runs**, with no button: there is nothing to choose, and a scanner that
  executes the page can only confirm a mailbox it really reached.
- **Reset asks for the password once, with "Show password"**, not a second field. The hint states both minimums;
  the API's reasons are shown when it refuses (only reachable with a valid link, 0028).
- **Success goes to `/signin` by router navigation**, carrying `'password-changed'` or `'email-confirmed'` in the
  navigation's state, read from the router (not `history.state`, which a reload would replay). No `returnUrl`: the
  trip through email has already lost it, and `/signin` falls back to `/admin/` (0027).
- **An outage or a rate limit keeps the link** ("your link still works"); only a refusal drops it.
- `/signin` gains "Forgot your password?". The four pages share `AccountPanel` (the frame `/signin` had inline) and
  `EmailRequestForm`.

### A new confirmation link, by address

`POST /bff/resend-confirmation` `{ email }` → **always 202**, built exactly like `/bff/forgot-password`:

- The gateway's hand-off queue becomes generic: `AccountEmailRequests` / `AccountEmailQueue` /
  `AccountEmailDispatcher`, carrying an `AccountEmailKind`. **Each kind has its own cooldown key**
  (`6311:password-reset-cooldown:…`, `6311:email-confirmation-cooldown:…`), so asking for one never holds back the
  other. They share the queue's capacity, the dispatcher and the `bff-account` rate limit.
- **Configuration is renamed `Gateway:PasswordReset` → `Gateway:AccountEmail`** (same `Cooldown` and `QueueCapacity`),
  because it no longer configures only resets.
- The API route is `POST /api/v1/internal/identity/email-confirmation` `{ email }` → `202`
  (`IIdentityFacade.ResendEmailConfirmationAsync`). An unknown address **and an already-confirmed one** stage nothing
  and write nothing; an unconfirmed one stages the same `email.send-requested` message account creation does, and an
  `identity.email-confirmation-requested` audit row.
- The page's words promise nothing that differs: "If that address has an account waiting to be confirmed, we've sent
  it a new link… If you've already confirmed your address, you can sign in."

## Options considered

| Option | Cost / trade-off |
|---|---|
| **Capture in `main.ts` before bootstrap (chosen)** | Nothing in the app ever sees the fragment. A module variable outside DI, behind an injectable for tests. |
| Read `location.hash` in the page's `afterNextRender` | The router and anchor scrolling have already read it by then. |
| Keep the link in `sessionStorage` to survive a refresh | Puts a credential in browser storage, which auth.md forbids. |
| **Resend by address (chosen)** | A second request-by-address surface, closed the same way as forgot-password. |
| Resend by the failed link's user id | Works only while the page still holds the id: not after a refresh, and never from `/signin`. |
| Treat a successful reset as confirming the address | Would help only people who also forgot their password; 0028 left it as later hardening. |
| No resend; "ask whoever set up your account" | Leaves an unconfirmed Editor locked out until an Admin acts, with no user-management screen yet. |
| **Navigation state for the `/signin` notice (chosen)** | Invisible in the URL and not replayed by a reload. |
| A `?reset=1` query flag | Visible, bookmarkable, and one more untrusted input for `/signin` to read. |

## Consequences

- **The emailed links work end to end**: forgot → Mailpit → reset → `/signin` → `/admin/`.
- **0028's queue, cooldown and configuration now cover two kinds**; its decision stands, its names changed.
  `outbox.md`'s one sanctioned non-outbox queue is this same queue.
- **A refresh on a link page loses the link.** Accepted, and said on the page.
- **The browser's own history may still list the full link**, recorded when the email was opened, before any script
  ran; `replaceState` cannot reach that. Accepted: a reset token is spent by use and both last an hour.
- **Until 3.2, the browser's own console shows the full link.** The CSP blocks the site's inline scripts (0027
  owes them a hash or nonce), and the browser's violation message names the document URL, fragment included. It is
  written while `<head>` parses, before any app code can run, so no capture can come earlier. It stays in that
  person's own console (there is no `report-uri` / `report-to`, so nothing is sent anywhere), and it disappears when
  3.2 makes the inline scripts allowed. Found walking the flow through the gateway; 3.2 should re-check it.
- **A stale antiforgery pair that survives the one retry is "unavailable", not "link-invalid"**: it is a 400 like
  a refused link, and reading it as one would drop credentials that still work (found by the security review).
- **Without JavaScript the link pages cannot work** and show their pre-hydration state. There is deliberately no
  `<noscript>` note, for 0027's reason: hydration fails on its parsed content.
- **Anyone can make the site email any address**, at most one reset and one confirmation per address per cooldown
  window, and at most the `bff-account` limit per client. The same bound 0028 accepted for resets.
- **The confirmation resend has no "who asked" record for unknown or confirmed addresses**, by design.
- **Still owed:** a "your password was changed" email, and an Admin-initiated resend from user management (which
  can use the existing `RequestEmailConfirmationAsync(userId)`).
