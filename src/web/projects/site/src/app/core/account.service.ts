import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  ANTIFORGERY_REQUIRED,
  postWithAntiforgery,
  problemCode,
  problemErrors,
  retryAfterSeconds,
} from './bff-problem';
import { LinkCredentials } from './link-fragment';

/** Which email someone asked for by address. */
export type AccountEmailKind = 'password-reset' | 'email-confirmation';

type Busy =
  | { readonly kind: 'rate-limited'; readonly retryAfterSeconds: number | null }
  | { readonly kind: 'unavailable' };

/**
 * A request for an email. `sent` is the gateway's one answer for every well-formed address, registered or not
 * (ADR 0028, 0029): nothing here, or on the page, can tell the two apart.
 */
export type AccountEmailResult =
  { readonly kind: 'sent' } | { readonly kind: 'invalid-address' } | Busy;

/** Using an emailed link. Every unusable link is one `link-invalid`; the gateway says no more than that. */
export type LinkResult = { readonly kind: 'done' } | { readonly kind: 'link-invalid' } | Busy;

/** Setting a new password with a link: as {@link LinkResult}, plus the policy's reasons, which need a valid link. */
export type PasswordResetResult =
  LinkResult | { readonly kind: 'password-rejected'; readonly reasons: readonly string[] };

const ROUTES: Record<AccountEmailKind, string> = {
  'password-reset': '/bff/forgot-password',
  'email-confirmation': '/bff/resend-confirmation',
};

const TOKEN_INVALID = 'auth.token_invalid';
const PASSWORD_REJECTED = 'auth.password_rejected';

/**
 * The gateway's emailed-link endpoints (auth.md "Emailed links", ADR 0028, 0029). Browser only.
 *
 * The token and the new password exist in the one request body that carries them, and never in a URL: the routes
 * are fixed paths with no query.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);

  async requestEmail(kind: AccountEmailKind, email: string): Promise<AccountEmailResult> {
    try {
      await postWithAntiforgery(this.http, ROUTES[kind], { email });
      return { kind: 'sent' };
    } catch (error) {
      // Only a missing or oversized address is a 400 here, which says nothing about any account. A stale pair that
      // survived the retry is a 400 too, and is not the address's fault.
      if (problemCode(error) === ANTIFORGERY_REQUIRED) return { kind: 'unavailable' };
      if (error instanceof HttpErrorResponse && error.status === 400)
        return { kind: 'invalid-address' };
      return busy(error);
    }
  }

  async resetPassword(link: LinkCredentials, newPassword: string): Promise<PasswordResetResult> {
    try {
      await postWithAntiforgery(this.http, '/bff/reset-password', {
        userId: link.userId,
        token: link.token,
        newPassword,
      });
      return { kind: 'done' };
    } catch (error) {
      if (problemCode(error) === PASSWORD_REJECTED) {
        return { kind: 'password-rejected', reasons: problemErrors(error, 'newPassword') };
      }
      return linkFailure(error);
    }
  }

  async confirmEmail(link: LinkCredentials): Promise<LinkResult> {
    try {
      await postWithAntiforgery(this.http, '/bff/confirm-email', {
        userId: link.userId,
        token: link.token,
      });
      return { kind: 'done' };
    } catch (error) {
      return linkFailure(error);
    }
  }
}

/**
 * `auth.token_invalid`, and any other 400: the only other one is `validation.failed` for a link with a missing or
 * oversized part, which is just as unusable. Anything else is not an answer about the link — an outage told as
 * "this link has expired" would send people round the reset loop for nothing.
 */
function linkFailure(error: unknown): LinkResult {
  const code = problemCode(error);
  if (code === TOKEN_INVALID) return { kind: 'link-invalid' };
  // A stale antiforgery pair that survived the retry is also a 400, but says nothing about the link. Read as
  // "link-invalid" it would drop credentials that still work, and the fragment is gone, so they could not come back.
  if (code === ANTIFORGERY_REQUIRED) return { kind: 'unavailable' };
  if (error instanceof HttpErrorResponse && error.status === 400) return { kind: 'link-invalid' };
  return busy(error);
}

function busy(error: unknown): Busy {
  if (error instanceof HttpErrorResponse && error.status === 429) {
    return { kind: 'rate-limited', retryAfterSeconds: retryAfterSeconds(error) };
  }
  return { kind: 'unavailable' };
}
