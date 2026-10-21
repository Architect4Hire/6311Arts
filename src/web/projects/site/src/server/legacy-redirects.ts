/** One redirect as the site API lists it (`GET /api/v1/site/redirects`, ADR 0035): old address in normal form. */
export interface SiteRedirect {
  fromPath: string;
  toPath: string;
  statusCode: number;
}

export interface RedirectHit {
  location: string;
  status: number;
}

/**
 * The spike's hard-coded map, kept as the fallback (frontend.md "Legacy URL redirects"): what still 301s if the API
 * cannot be read. The same four are seeded into the Redirects table in every environment (ADR 0035), so normally the
 * CMS's copy answers first.
 */
export const SPIKE_REDIRECTS: Readonly<Record<string, string>> = {
  '/events': '/classes',
  '/gallery': '/exhibitions',
  '/submit-your-work': '/submit',
  '/privacy-policy': '/privacy',
};

/**
 * An address in the Redirects module's normal form (`RedirectPaths`): lower case, no trailing slash, the query kept.
 * So `/Gallery/` finds the redirect staff stored as `/gallery`.
 */
export function normalizeAddress(url: string): { path: string; withQuery: string } {
  const question = url.indexOf('?');
  const rawPath = question < 0 ? url : url.slice(0, question);
  const query = question < 0 ? '' : url.slice(question);
  let path: string;
  try {
    path = decodeURI(rawPath).toLowerCase();
  } catch {
    path = rawPath.toLowerCase();
  }
  path = path.length > 1 ? path.replace(/\/+$/, '') || '/' : path;
  return { path, withQuery: query && query !== '?' ? path + query : path };
}

/**
 * Where an old address goes, or `null` to render the page. The CMS's redirects come first (with the query, then
 * without), then the spike's rules: its fixed map, WooCommerce product URLs (a class's slug goes to `/classes`, any
 * other to `/shop`) and artist archives. A redirect to the address asked for is never answered, so a bad row can't loop.
 *
 * @param classSlugs Published classes' slugs, from the sitemap: how a `/product/{slug}` URL tells a class from a piece.
 */
export function resolveRedirect(
  url: string,
  redirects: readonly SiteRedirect[],
  classSlugs: ReadonlySet<string>,
): RedirectHit | null {
  const { path, withQuery } = normalizeAddress(url);
  const hit = find(path, withQuery, redirects, classSlugs);
  return hit && hit.location !== path && hit.location !== withQuery ? hit : null;
}

function find(
  path: string,
  withQuery: string,
  redirects: readonly SiteRedirect[],
  classSlugs: ReadonlySet<string>,
): RedirectHit | null {
  const cms =
    redirects.find((r) => r.fromPath === withQuery) ?? redirects.find((r) => r.fromPath === path);
  if (cms) return { location: cms.toPath, status: cms.statusCode === 302 ? 302 : 301 };

  if (SPIKE_REDIRECTS[path]) return { location: SPIKE_REDIRECTS[path], status: 301 };

  let match = path.match(/^\/product\/([^/]+)$/);
  if (match)
    return {
      location: classSlugs.has(match[1]) ? `/classes/${match[1]}` : `/shop/${match[1]}`,
      status: 301,
    };

  match = path.match(/^\/artist\/([^/]+)(?:\/page\/\d+)?$/);
  if (match) return { location: `/artists/${match[1]}`, status: 301 };

  return null;
}
