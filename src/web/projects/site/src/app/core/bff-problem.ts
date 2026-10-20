import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** The gateway's code for a missing or stale antiforgery pair (ADR 0024). */
export const ANTIFORGERY_REQUIRED = 'auth.antiforgery_required';

/**
 * POSTs JSON to a `/bff` route the way every unsafe request must go (auth.md "CSRF"): fetch a fresh antiforgery
 * pair for whoever the browser is now, then post with the header Angular's XSRF interceptor adds. A refusal for a
 * stale pair (a key-ring rotation, a dropped cookie) fetches a new one and tries exactly once more; every other
 * failure is thrown to the caller, which classifies it.
 */
export async function postWithAntiforgery<T>(
  http: HttpClient,
  url: string,
  body: object,
): Promise<T> {
  const attempt = async () => {
    await firstValueFrom(http.get('/bff/antiforgery', { observe: 'response' }));
    return firstValueFrom(http.post<T>(url, body));
  };

  try {
    return await attempt();
  } catch (error) {
    if (problemCode(error) !== ANTIFORGERY_REQUIRED) throw error;
    return attempt();
  }
}

/** The ProblemDetails `code` of a failed request, or `null`. */
export function problemCode(error: unknown): string | null {
  const body = problemBody(error);
  const code = body?.['code'];
  return typeof code === 'string' ? code : null;
}

/** The messages under `errors[field]` of a failed request, keeping only strings. */
export function problemErrors(error: unknown, field: string): string[] {
  const errors = problemBody(error)?.['errors'];
  if (typeof errors !== 'object' || errors === null) return [];
  const messages = (errors as Record<string, unknown>)[field];
  return Array.isArray(messages)
    ? messages.filter((message): message is string => typeof message === 'string')
    : [];
}

/** A 429's `Retry-After`, in seconds, when it is a usable number. */
export function retryAfterSeconds(error: HttpErrorResponse): number | null {
  const seconds = Number(error.headers.get('Retry-After'));
  return Number.isFinite(seconds) && seconds > 0 ? seconds : null;
}

function problemBody(error: unknown): Record<string, unknown> | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body: unknown = error.error;
  return typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null;
}
