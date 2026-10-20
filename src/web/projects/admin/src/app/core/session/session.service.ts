import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Role, STAFF_ROLES, Session, decodeSession } from '../../models/session';
import { BrowserNavigation } from '../browser/browser-navigation';
import { SESSION_PROBE, SUPPRESS_PROBLEM_TOAST } from '../http/http-context';

export type SessionState =
  | { readonly status: 'loading' }
  | { readonly status: 'signed-in'; readonly session: Session }
  | { readonly status: 'signed-out' }
  | { readonly status: 'unavailable' };

/**
 * Who is signed in, from the gateway's `/bff/session` (auth.md "Browser session").
 *
 * The browser holds nothing but the `__Host-6311.session` cookie, which script cannot read; this service holds the
 * display name and roles the gateway chose to show, in memory only. Nothing is written to browser storage, so a
 * reload asks the gateway again — the session's authority stays on the server.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly navigation = inject(BrowserNavigation);
  private readonly current = signal<SessionState>({ status: 'loading' });
  private inFlight: Promise<SessionState> | null = null;

  readonly state = this.current.asReadonly();
  readonly session = computed(() => {
    const state = this.current();
    return state.status === 'signed-in' ? state.session : null;
  });
  readonly isStaff = computed(() => this.hasAnyRole(STAFF_ROLES));

  hasAnyRole(roles: readonly Role[]): boolean {
    const session = this.session();
    return session !== null && roles.some((role) => session.roles.includes(role));
  }

  /** Loads the session once; later callers share the answer. `reload` asks again. */
  ensureLoaded(): Promise<SessionState> {
    const state = this.current();
    if (state.status !== 'loading') return Promise.resolve(state);
    return (this.inFlight ??= this.fetch());
  }

  reload(): Promise<SessionState> {
    this.current.set({ status: 'loading' });
    this.inFlight = null;
    return this.ensureLoaded();
  }

  /**
   * The gateway answered 401 to something the CMS asked for: the session ended (idle, absolute limit, a password
   * reset, "log out everywhere"). Go and sign in again, then come back to the same screen.
   */
  expired(): void {
    if (this.current().status === 'signed-out') return;
    this.current.set({ status: 'signed-out' });
    this.navigation.toSignIn(this.navigation.currentPath());
  }

  /** Ends this session. The gateway deletes the ticket, so a copied cookie dies with it. */
  async signOut(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/bff/logout', null));
    this.leave();
  }

  /** Ends every session this person has. The others stop at their next revalidation (ADR 0024). */
  async signOutEverywhere(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/bff/logout-everywhere', null));
    this.leave();
  }

  private leave(): void {
    this.current.set({ status: 'signed-out' });
    this.navigation.toSignIn();
  }

  private async fetch(): Promise<SessionState> {
    let next: SessionState;
    try {
      const body = await firstValueFrom(
        this.http.get<unknown>('/bff/session', {
          // "Not signed in" is an answer here, not a failure to toast or an ended session to leave.
          context: new HttpContext().set(SESSION_PROBE, true).set(SUPPRESS_PROBLEM_TOAST, true),
        }),
      );
      const session = decodeSession(body);
      next = session ? { status: 'signed-in', session } : { status: 'unavailable' };
    } catch (error) {
      next =
        error instanceof HttpErrorResponse && error.status === 401
          ? { status: 'signed-out' }
          : { status: 'unavailable' };
    } finally {
      this.inFlight = null;
    }
    this.current.set(next);
    return next;
  }
}
