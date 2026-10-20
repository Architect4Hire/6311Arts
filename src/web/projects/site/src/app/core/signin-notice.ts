import { Router } from '@angular/router';

/** Why someone was sent on to `/signin` from an emailed-link page. Not a secret, and only ever router state. */
export type SigninNotice = 'password-changed' | 'email-confirmed';

const STATE_KEY = 'signinNotice';

/** The navigation that takes someone from a finished link to `/signin`, with the notice it should show there. */
export function goToSignin(router: Router, notice: SigninNotice): Promise<boolean> {
  return router.navigate(['/signin'], { state: { [STATE_KEY]: notice } });
}

/**
 * The notice the navigation into `/signin` carried, read from the router rather than `history.state`, so a reload or
 * a later visit does not show it again. Call it while the navigation is still current (a page's constructor).
 */
export function arrivedWith(router: Router): SigninNotice | null {
  const notice: unknown = router.currentNavigation()?.extras.state?.[STATE_KEY];
  return notice === 'password-changed' || notice === 'email-confirmed' ? notice : null;
}
