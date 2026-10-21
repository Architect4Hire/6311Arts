import { InjectionToken, TransferState, inject, makeStateKey } from '@angular/core';

export interface SiteApiConfig {
  /** How long to wait for the API before treating a read as failed. */
  readonly timeoutMs: number;
  /**
   * Serve the bundled sample data (`../data/`) when a read fails, rather than an empty list and a 503. Off unless
   * the server's `SITE_MOCK_FALLBACK` is `true` (AppHost `Site:MockFallback`), for design work without a backend.
   */
  readonly mockFallback: boolean;
}

/** The server's flag, handed to the browser in the page so a client-side navigation behaves as the render did. */
export const MOCK_FALLBACK_KEY = makeStateKey<boolean>('site-api.mock-fallback');

export const SITE_API_TIMEOUT_MS = 4000;

/**
 * `SiteApi`'s settings. In the browser they come from the page the server rendered; the server provides its own
 * from the environment (`app.config.server.ts`), so nothing about them is bundled.
 */
export const SITE_API_CONFIG = new InjectionToken<SiteApiConfig>('SITE_API_CONFIG', {
  providedIn: 'root',
  factory: () => ({
    timeoutMs: SITE_API_TIMEOUT_MS,
    mockFallback: inject(TransferState).get(MOCK_FALLBACK_KEY, false),
  }),
});
