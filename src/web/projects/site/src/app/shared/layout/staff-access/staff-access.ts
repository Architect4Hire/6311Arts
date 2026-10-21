import { ChangeDetectionStrategy, Component, afterNextRender, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { BffSessionService, Session, isStaff } from '../../../core/bff-session.service';
import { CMS_HOME } from '../../../core/return-url';

type Access =
  /** No answer yet — and the state the server renders, because it never asks. */
  | { readonly kind: 'unknown' }
  | { readonly kind: 'anonymous' }
  | { readonly kind: 'staff'; readonly session: Session }
  /** Signed in without a CMS role: no CMS to offer, and a sign-in link would be nonsense. */
  | { readonly kind: 'other' };

/**
 * The footer's staff corner: "Staff sign-in" for a visitor, or the signed-in name and a link into the CMS
 * (`auth.md` "Browser session"). Deliberately small and in the footer bar — the CMS is for staff, and the main
 * nav belongs to visitors.
 *
 * **It renders nothing on the server, by design.** A page's SSR response carries `Cache-Control: s-maxage`, so
 * anything personal baked into the shell would be served to the next visitor from a shared cache
 * (`gateway.md` "Caching"). The session is asked for after hydration instead, and the server and the first
 * client render agree on empty, so hydration has nothing to mismatch (NG0500).
 *
 * The CMS opens in a new tab: staff reading the public site keep their place on it.
 */
@Component({
  selector: 'app-staff-access',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, UiIcon],
  templateUrl: './staff-access.html',
  styleUrl: './staff-access.scss',
})
export class StaffAccess {
  protected readonly cmsHome = CMS_HOME;

  private readonly sessions = inject(BffSessionService);
  protected readonly access = signal<Access>({ kind: 'unknown' });

  protected displayName(): string {
    const access = this.access();
    return access.kind === 'staff' ? access.session.displayName : '';
  }

  constructor() {
    afterNextRender(async () => {
      // Any failure counts as "nobody": this decides what the footer offers, never what anyone may reach.
      const session = await this.sessions.current();
      this.access.set(
        session === null
          ? { kind: 'anonymous' }
          : isStaff(session)
            ? { kind: 'staff', session }
            : { kind: 'other' },
      );
    });
  }
}
