import { HttpContextToken } from '@angular/common/http';

/**
 * Set on a request whose caller shows the failure itself — an editor's 409 conflict view, a form's field errors —
 * so the problem interceptor does not toast it as well. It means "no toast" and nothing more: a 401 still sends
 * the person to sign in.
 */
export const SUPPRESS_PROBLEM_TOAST = new HttpContextToken<boolean>(() => false);

/**
 * The one request whose 401 is an answer rather than an ending: `/bff/session` asking "is anyone signed in?".
 * The session guard acts on that answer, so the interceptor must not redirect for it.
 */
export const SESSION_PROBE = new HttpContextToken<boolean>(() => false);

/** Marks the one retry the antiforgery interceptor allows itself, so a persistent 400 cannot loop. */
export const ANTIFORGERY_RETRIED = new HttpContextToken<boolean>(() => false);
