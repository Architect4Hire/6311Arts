import { FetchBackend, HttpEvent, HttpRequest } from '@angular/common/http';
import { Injectable, InjectionToken, REQUEST, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { SITE_API_BASE } from '../services/site-api.service';

/**
 * The gateway's origin for server-side reads, from `SITE_API_ORIGIN` in the Node server's environment (Aspire sets
 * it). Server-only: provided by `app.config.server.ts`, so it is never in the browser bundle (frontend.md).
 */
export const SITE_API_ORIGIN = new InjectionToken<string | null>('SITE_API_ORIGIN');

/**
 * The server's HTTP backend: Angular's fetch backend, except that a site API read is sent to the gateway.
 *
 * `SiteApi` asks for relative `/api/v1/site/...` everywhere, because the HTTP transfer cache keys a response by the
 * URL it was asked for and the browser asks relatively. The backend runs after every interceptor — the transfer
 * cache included — so re-targeting here leaves the cache key alone. Angular's server platform may already have
 * resolved the address against the page being rendered (its own origin, not the gateway's), so that form is
 * re-targeted too. Anything else passes through untouched.
 */
@Injectable()
export class GatewayBackend extends FetchBackend {
  private readonly origin = inject(SITE_API_ORIGIN);
  private readonly request = inject(REQUEST, { optional: true });

  override handle(req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> {
    const path = this.siteApiPath(req.url);
    if (path === null) return super.handle(req);
    if (!this.origin) {
      // No gateway to ask (a build-time prerender, a server started by hand): a failed read, which SiteApi handles.
      return throwError(() => new Error('SITE_API_ORIGIN is not set.'));
    }
    return super.handle(req.clone({ url: this.origin.replace(/\/+$/, '') + path }));
  }

  /** The path and query of a site API read addressed to this site, or `null` for any other request. */
  private siteApiPath(url: string): string | null {
    if (url.startsWith(SITE_API_BASE + '/')) return url;
    let parsed: URL;
    try {
      parsed = new URL(url);
    } catch {
      return null;
    }
    const own = this.request ? new URL(this.request.url).origin : null;
    return parsed.origin === own && parsed.pathname.startsWith(SITE_API_BASE + '/')
      ? parsed.pathname + parsed.search
      : null;
  }
}
