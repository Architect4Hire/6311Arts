import { SiteRedirect } from './legacy-redirects';
import { SitemapEntry } from './sitemap';

/** How long the server keeps what it routes by before it asks again: the API caches the redirects for five minutes itself. */
export const ROUTING_TTL_MS = 60_000;

/** How soon it asks again when the last try failed: the API is usually just still starting (the health check reads first). */
export const ROUTING_RETRY_MS = 5_000;

export const READ_TIMEOUT_MS = 4000;

/** What the server needs before it renders anything: the CMS's redirects, and which slugs are classes. */
export interface Routing {
  redirects: SiteRedirect[];
  classSlugs: Set<string>;
}

type Fetch = (
  url: string,
  init?: { signal?: AbortSignal },
) => Promise<{ ok: boolean; status?: number; json(): Promise<unknown> }>;

/**
 * The site server's reads of `/api/v1/site/*` that happen outside Angular: the redirects and class slugs it routes by
 * (on every request, before rendering) and the sitemap. They go to the gateway at `SITE_API_ORIGIN`, as SSR's reads do
 * (frontend.md).
 *
 * - **Routing** is kept and refreshed behind the request once a minute: only the very first request waits on the API,
 *   and when the API is down the last good copy — or nothing, and then the spike's map alone — still answers.
 * - **The sitemap** is read when asked for; `null` when it can't be, and the route answers 503 so crawlers come back.
 */
export function createSiteData(
  origin: string | null,
  fetchImpl: Fetch = fetch,
  now: () => number = Date.now,
) {
  let routing: { at: number; ttl: number; value: Routing } | null = null;
  let refreshing: Promise<void> | null = null;

  async function read<T>(path: string, decode: (body: unknown) => T | null): Promise<T | null> {
    if (!origin) return null;
    try {
      const response = await fetchImpl(origin.replace(/\/+$/, '') + path, {
        signal: AbortSignal.timeout(READ_TIMEOUT_MS),
      });
      if (!response.ok) {
        console.warn(`[site-data] ${path} answered ${response.status ?? 'not ok'}`);
        return null;
      }
      return decode(await response.json());
    } catch (error) {
      // The path and the error type only: never the gateway address or anything a message might carry.
      console.warn(
        `[site-data] reading ${path} failed (${error instanceof Error ? error.name : 'unknown'})`,
      );
      return null;
    }
  }

  function refresh(): Promise<void> {
    refreshing ??= Promise.all([
      read('/api/v1/site/redirects', decodeRedirects),
      read('/api/v1/site/sitemap', decodeSitemap),
    ])
      .then(([redirects, sitemap]) => {
        // A failed read keeps the last good part, and still counts as a try: the API is asked again in a few seconds,
        // not on every request.
        routing = {
          at: now(),
          ttl: redirects && sitemap ? ROUTING_TTL_MS : ROUTING_RETRY_MS,
          value: {
            redirects: redirects ?? routing?.value.redirects ?? [],
            classSlugs: sitemap ? classSlugsOf(sitemap) : (routing?.value.classSlugs ?? new Set()),
          },
        };
      })
      .finally(() => (refreshing = null));
    return refreshing;
  }

  return {
    /** The redirects and class slugs: the cached copy at once, refreshed behind the request when it is a minute old. */
    async routing(): Promise<Routing> {
      if (!routing) await refresh();
      else if (now() - routing.at >= routing.ttl) void refresh();
      return routing?.value ?? { redirects: [], classSlugs: new Set() };
    },

    /** Every published record's address, or `null` when the API can't be read. */
    sitemap(): Promise<SitemapEntry[] | null> {
      return read('/api/v1/site/sitemap', decodeSitemap);
    },
  };
}

function decodeRedirects(body: unknown): SiteRedirect[] | null {
  if (!Array.isArray(body)) return null;
  return body.filter(
    (r): r is SiteRedirect =>
      typeof r === 'object' &&
      r !== null &&
      typeof (r as SiteRedirect).fromPath === 'string' &&
      typeof (r as SiteRedirect).toPath === 'string' &&
      typeof (r as SiteRedirect).statusCode === 'number',
  );
}

function decodeSitemap(body: unknown): SitemapEntry[] | null {
  if (!Array.isArray(body)) return null;
  return body.filter(
    (e): e is SitemapEntry =>
      typeof e === 'object' &&
      e !== null &&
      typeof (e as SitemapEntry).path === 'string' &&
      (e as SitemapEntry).path.startsWith('/') &&
      typeof (e as SitemapEntry).lastModified === 'string',
  );
}

/** Published classes' slugs, from the sitemap's `/classes/{slug}` addresses. */
export function classSlugsOf(entries: readonly SitemapEntry[]): Set<string> {
  return new Set(
    entries.flatMap((entry) => {
      const match = entry.path.match(/^\/classes\/([^/]+)$/);
      return match ? [match[1]] : [];
    }),
  );
}
