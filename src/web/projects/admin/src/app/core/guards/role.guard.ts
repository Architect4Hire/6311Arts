import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../../models/session';
import { SessionService } from '../session/session.service';

/**
 * A section for some staff roles only (auth.md: Admin does sync replay, deletes and user management). Runs under
 * `sessionGuard`, so the session is already loaded. This only shapes the UI: the API's own policy is the lock.
 */
export function roleGuard(...roles: readonly Role[]): CanActivateFn {
  return () => {
    const session = inject(SessionService);
    return session.hasAnyRole(roles) || inject(Router).createUrlTree(['/forbidden']);
  };
}
