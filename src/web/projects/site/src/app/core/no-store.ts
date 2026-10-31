import { RESPONSE_INIT, inject } from '@angular/core';

/**
 * Marks the server-rendered response `Cache-Control: no-store`, for pages that answer per person: sign-in and the
 * emailed-link pages. `server.ts` respects a page's own header rather than adding its `s-maxage` (ADR 0027). Call it
 * from a page's constructor; in the browser there is no response, and it does nothing.
 */
export function noStore(): void {
  setHeaders({ 'Cache-Control': 'no-store' });
}

/**
 * `no-store` and `X-Robots-Tag: noindex, nofollow`, for the preview page (ADR 0036): a draft is neither kept by a
 * shared cache nor indexed, and the header says so to anything that never reads the HTML. The API's own answer
 * carries both already; this is the page the person actually opens.
 */
export function noStoreNoIndex(): void {
  setHeaders({ 'Cache-Control': 'no-store', 'X-Robots-Tag': 'noindex, nofollow' });
}

function setHeaders(values: Record<string, string>): void {
  const init = inject(RESPONSE_INIT, { optional: true });
  if (!init) return;
  const headers = new Headers(init.headers);
  for (const [name, value] of Object.entries(values)) headers.set(name, value);
  init.headers = headers;
}
