import { RESPONSE_INIT, inject } from '@angular/core';

/**
 * Marks the server-rendered response `Cache-Control: no-store`, for pages that answer per person: sign-in and the
 * emailed-link pages. `server.ts` respects a page's own header rather than adding its `s-maxage` (ADR 0027). Call it
 * from a page's constructor; in the browser there is no response, and it does nothing.
 */
export function noStore(): void {
  const init = inject(RESPONSE_INIT, { optional: true });
  if (!init) return;
  const headers = new Headers(init.headers);
  headers.set('Cache-Control', 'no-store');
  init.headers = headers;
}
