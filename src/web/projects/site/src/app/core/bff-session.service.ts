import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { postWithAntiforgery, retryAfterSeconds } from './bff-problem';

/**
 * What `/bff/session` and a successful `/bff/login` return, and the whole of it: a display name and roles, never
 * an id, an email address or a token (auth.md). Mirrors the gateway's `SessionResponse`.
 */
export interface Session {
  readonly displayName: string;
  readonly roles: readonly string[];
}

/** The roles that reach the CMS: the gateway's `CmsEditor` policy (`GatewayRoles.Staff`). */
export const STAFF_ROLES: readonly string[] = ['Admin', 'Editor'];

export function isStaff(session: Session): boolean {
  return session.roles.some((role) => STAFF_ROLES.includes(role));
}

export type SignInResult =
  | { readonly kind: 'signed-in'; readonly session: Session }
  /** Wrong address or password, a lockout or an unconfirmed staff address: the gateway gives one answer. */
  | { readonly kind: 'refused' }
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number | null }
  | { readonly kind: 'unavailable' };

/**
 * The gateway's browser-session endpoints, for the staff sign-in page (auth.md "Browser session", ADR 0024).
 * Browser only: nothing here runs during server rendering.
 *
 * The browser receives an opaque `__Host-6311.session` cookie and nothing else; the password exists only in the
 * one request body that carries it. The XSRF header comes from Angular's interceptor (`app.config.ts`).
 */
@Injectable({ providedIn: 'root' })
export class BffSessionService {
  private readonly http = inject(HttpClient);

  /** Who is signed in, or `null`. Any failure counts as "nobody": this only decides what the page offers. */
  async current(): Promise<Session | null> {
    try {
      return decodeSession(await firstValueFrom(this.http.get<unknown>('/bff/session')));
    } catch {
      return null;
    }
  }

  async signIn(email: string, password: string): Promise<SignInResult> {
    try {
      // Login is an unsafe request like any other, so it needs a fresh antiforgery pair before it can be sent.
      const body = await postWithAntiforgery<unknown>(this.http, '/bff/login', { email, password });
      const session = decodeSession(body);
      return session ? { kind: 'signed-in', session } : { kind: 'unavailable' };
    } catch (error) {
      return classify(error);
    }
  }
}

function decodeSession(body: unknown): Session | null {
  if (typeof body !== 'object' || body === null) return null;
  const { displayName, roles } = body as Record<string, unknown>;
  if (typeof displayName !== 'string' || !Array.isArray(roles)) return null;
  if (!roles.every((role): role is string => typeof role === 'string')) return null;
  return { displayName, roles };
}

function classify(error: unknown): SignInResult {
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unavailable' };
  switch (error.status) {
    case 400:
    case 401:
      return { kind: 'refused' };
    case 429:
      return { kind: 'rate-limited', retryAfterSeconds: retryAfterSeconds(error) };
    default:
      return { kind: 'unavailable' };
  }
}
