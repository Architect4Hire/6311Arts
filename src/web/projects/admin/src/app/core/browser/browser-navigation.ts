import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** The staff sign-in page. On the public site, never under the gated `/admin/**` (ADR 0024). */
export const SIGN_IN_PATH = '/signin';

/**
 * Leaving the SPA: the sign-in page is another app, so getting there is a full navigation, not a router one.
 * Behind an injectable so tests can watch where the CMS sends people without leaving the test page.
 */
@Injectable({ providedIn: 'root' })
export class BrowserNavigation {
  private readonly location = inject(DOCUMENT).location;

  /** The path and query the browser is on, including the `/admin` base. */
  currentPath(): string {
    return this.location.pathname + this.location.search;
  }

  /** To `/signin`, asking to come back to `returnPath` (a path on this origin) afterwards. */
  toSignIn(returnPath?: string): void {
    this.assign(
      returnPath ? `${SIGN_IN_PATH}?returnUrl=${encodeURIComponent(returnPath)}` : SIGN_IN_PATH,
    );
  }

  assign(url: string): void {
    this.location.assign(url);
  }
}
