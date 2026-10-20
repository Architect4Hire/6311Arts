import { LocationStrategy } from '@angular/common';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { BrowserNavigation } from '../browser/browser-navigation';
import { SessionService } from '../session/session.service';

/**
 * The CMS needs a staff session. The gateway already refuses `/admin/**` without one (ADR 0024), so this guard
 * mostly meets sessions that ended while the SPA was open, or a gateway that could not answer.
 *
 * - signed out → `/signin`, coming back to the URL that was asked for
 * - signed in without Admin or Editor → the CMS's "not available" page
 * - the gateway could not say → the "unavailable" page, which offers a retry
 */
export const sessionGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const navigation = inject(BrowserNavigation);
  const locationStrategy = inject(LocationStrategy);

  const current = await session.ensureLoaded();

  switch (current.status) {
    case 'signed-in':
      return session.isStaff() ? true : router.createUrlTree(['/forbidden']);
    case 'signed-out':
      // The full path, base href included: the sign-in page is another app on the same origin.
      navigation.toSignIn(locationStrategy.prepareExternalUrl(state.url));
      return false;
    default:
      return router.createUrlTree(['/unavailable'], { queryParams: { from: state.url } });
  }
};
