import { Injectable } from '@angular/core';

/** The user id and token an emailed link carries in its fragment (ADR 0028). */
export interface LinkCredentials {
  readonly userId: string;
  readonly token: string;
}

/** The pages an emailed link opens. Nothing else is ever read from a fragment. */
export const LINK_PATHS: readonly string[] = ['/account/reset-password', '/account/confirm-email'];

/** The gateway's ceilings (`BffEndpoints.AccountLimits`): anything longer is not a link we sent. */
const USER_ID_MAX_LENGTH = 450;
const TOKEN_MAX_LENGTH = 2048;

let captured: LinkCredentials | null = null;

/**
 * Reads an emailed link's credentials out of the address bar and removes them from it, before the app starts
 * (`main.ts`, ADR 0029).
 *
 * Before bootstrap, not in a component: by the time any component runs, the router has already read the URL and
 * would hold the token as the route's fragment, and anchor scrolling would have looked for an element named after
 * it. Here nothing in the app has seen it yet. `replaceState` also takes it out of this history entry, so Back and
 * Forward never bring it back.
 *
 * The fragment never reaches the server: browsers do not send it, so the server-rendered page cannot contain it.
 * The cost is that a refresh loses the link, because it is kept nowhere but in memory; the page then offers a new
 * one.
 */
export function captureLinkFragment(
  location: Pick<Location, 'pathname' | 'search' | 'hash'>,
  history: Pick<History, 'replaceState' | 'state'>,
): void {
  if (!LINK_PATHS.includes(trimTrailingSlash(location.pathname))) return;
  const hash = location.hash;
  if (!hash) return;

  // First, whatever the fragment holds: a malformed link is cleared as surely as a good one.
  history.replaceState(history.state, '', location.pathname + location.search);
  captured = parseLinkFragment(hash);
}

/**
 * `#userId=…&token=…`, each value URL-encoded by the composer (`IdentityEmailComposers.BuildLink`). Split by hand
 * rather than with `URLSearchParams`, which reads `+` as a space: Identity's tokens are Base64, and a `+` that
 * reached us unencoded would otherwise be silently changed.
 */
export function parseLinkFragment(hash: string): LinkCredentials | null {
  const values = new Map<string, string>();
  for (const pair of hash.replace(/^#/, '').split('&')) {
    const at = pair.indexOf('=');
    if (at <= 0) continue;
    try {
      values.set(pair.slice(0, at), decodeURIComponent(pair.slice(at + 1)));
    } catch {
      return null;
    }
  }

  const userId = values.get('userId');
  const token = values.get('token');
  if (!userId || !token) return null;
  if (userId.length > USER_ID_MAX_LENGTH || token.length > TOKEN_MAX_LENGTH) return null;
  return { userId, token };
}

function trimTrailingSlash(path: string): string {
  return path.length > 1 ? path.replace(/\/+$/, '') : path;
}

/**
 * Hands the captured credentials to the page that needs them, once. Behind an injectable so tests can give a page
 * a link without touching the real address bar. On the server there is never anything to take.
 */
@Injectable({ providedIn: 'root' })
export class LinkFragment {
  take(): LinkCredentials | null {
    const credentials = captured;
    captured = null;
    return credentials;
  }
}
