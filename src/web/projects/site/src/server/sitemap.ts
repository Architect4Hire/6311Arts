/** One published record's address and last change, as `GET /api/v1/site/sitemap` lists it (ADR 0042). */
export interface SitemapEntry {
  path: string;
  lastModified: string;
}

/** The pages that are always there: the spike's list. Records' own pages come from the API. */
export const FIXED_PAGES: readonly string[] = [
  '/',
  '/classes',
  '/exhibitions',
  '/artists',
  '/shop',
  '/memberships',
  '/about',
  '/visit',
  '/submit',
];

const ESCAPES: Readonly<Record<string, string>> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&apos;',
};

const escapeXml = (text: string) => text.replace(/[&<>"']/g, (c) => ESCAPES[c]);

/**
 * `/sitemap.xml`: the fixed pages, then every published record (with `<lastmod>`), each address once. Previews,
 * drafts and redirects never appear: the API lists only what is published (ADR 0042).
 */
export function buildSitemap(siteUrl: string, entries: readonly SitemapEntry[]): string {
  const seen = new Set<string>();
  const urls: string[] = [];
  const add = (path: string, lastModified?: string) => {
    if (seen.has(path)) return;
    seen.add(path);
    const lastmod = lastModified
      ? `<lastmod>${escapeXml(lastModified.slice(0, 10))}</lastmod>`
      : '';
    urls.push(`  <url><loc>${escapeXml(siteUrl + path)}</loc>${lastmod}</url>`);
  };
  for (const path of FIXED_PAGES) add(path);
  for (const entry of entries) add(entry.path, entry.lastModified);
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.join('\n')}\n</urlset>\n`;
}
