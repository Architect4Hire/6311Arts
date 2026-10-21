import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';
import { buildSitemap } from './server/sitemap';
import { resolveRedirect } from './server/legacy-redirects';
import { createSiteData } from './server/site-data';

const browserDistFolder = join(import.meta.dirname, '../browser');
const SITE_URL = process.env['SITE_URL'] ?? 'https://6311arts.com';

/** The gateway, the only way into the API (ADR 0001): Aspire hands it to this server, as it does to SSR (ADR 0046). */
const siteData = createSiteData(process.env['SITE_API_ORIGIN'] || null);

const app = express();
const angularApp = new AngularNodeAppEngine();

app.disable('x-powered-by');

/**
 * 301s from old addresses, so existing Google rankings, Instagram links and printed flyers keep working. The CMS's
 * redirects come first (`/api/v1/site/redirects`: staff's, imported and slug changes, ADR 0035), then the spike's
 * rules as the fallback — its fixed map, WooCommerce product URLs (a class's slug goes to /classes, any other to /shop)
 * and artist archives. See server/legacy-redirects.ts.
 */
app.use(async (req, res, next) => {
  const { redirects, classSlugs } = await siteData.routing();
  const hit = resolveRedirect(req.url, redirects, classSlugs);
  if (hit) return res.redirect(hit.status, hit.location);
  next();
});

app.get('/robots.txt', (_req, res) => {
  // /preview/ and /admin/ are disallowed as well as /api/. A preview link is a capability URL (ADR 0036) and
  // nothing should carry one into an index; the CMS is session-gated at the gateway, but a crawler following a
  // link to it just generates sign-in redirects. Both are belt-and-braces: the preview response also sets
  // `X-Robots-Tag: noindex` and `no-store`, and 3.x's preview page must keep doing so (security review).
  res.type('text/plain').send(
    `User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /preview/\nDisallow: /admin/\n\nSitemap: ${SITE_URL}/sitemap.xml\n`,
  );
});

/**
 * The sitemap from the site read API (`/api/v1/site/sitemap`, ADR 0042): the fixed pages and every published record
 * with its last change. When the API can't be read it is a 503 with `Retry-After`, never a short list a crawler would
 * take as pages removed.
 */
app.get('/sitemap.xml', async (_req, res) => {
  const entries = await siteData.sitemap();
  if (!entries) {
    return res.status(503).set('Retry-After', '300').set('Cache-Control', 'no-store').type('text/plain').send('Try again shortly.\n');
  }
  return res.type('application/xml').set('Cache-Control', 'public, max-age=3600').send(buildSitemap(SITE_URL, entries));
});

/** Hashed build assets are immutable; everything else is short-lived. */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
    setHeaders: (res, path) => {
      if (!/-[A-Z0-9]{8}\.(js|css)$/.test(path)) res.setHeader('Cache-Control', 'public, max-age=3600');
    },
  }),
);

app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => {
      if (!response) return next();
      // Let a CDN serve pages for 5 minutes and refresh in the background, so the API is asked rarely.
      if (response.status === 200 && !response.headers.has('Cache-Control')) {
        response.headers.set('Cache-Control', 'public, max-age=0, s-maxage=300, stale-while-revalidate=86400');
      }
      return writeResponseToNodeResponse(response, res);
    })
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }
    console.log(`6311arts listening on http://localhost:${port}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
