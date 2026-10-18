import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';
import { MOCK_CLASSES } from './app/data/mock-classes';
import { MOCK_ARTISTS } from './app/data/mock-shop';

const browserDistFolder = join(import.meta.dirname, '../browser');
const SITE_URL = process.env['SITE_URL'] ?? 'https://6311arts.com';
const WP_ORIGIN = process.env['WP_ORIGIN'] ?? 'https://6311arts.com';

const app = express();
const angularApp = new AngularNodeAppEngine();

app.disable('x-powered-by');
app.use(express.json({ limit: '20kb' }));

/**
 * 301s from the old WordPress URLs, so existing Google rankings, Instagram links and printed flyers keep working.
 * Product URLs are shared by shop items and classes in WooCommerce; class slugs go to /classes.
 */
let classSlugCache = { at: 0, slugs: new Set(MOCK_CLASSES.map((c) => c.slug)) };
async function classSlugs(): Promise<Set<string>> {
  if (Date.now() - classSlugCache.at < 5 * 60_000) return classSlugCache.slugs;
  try {
    const r = await fetch(`${WP_ORIGIN}/wp-json/6311/v1/classes`, { signal: AbortSignal.timeout(4000) });
    if (r.ok) classSlugCache = { at: Date.now(), slugs: new Set(((await r.json()) as { slug: string }[]).map((c) => c.slug)) };
  } catch {
    classSlugCache.at = Date.now(); // keep the last known list and retry later
  }
  return classSlugCache.slugs;
}
const REDIRECTS: Record<string, string> = {
  '/events': '/classes',
  '/gallery': '/exhibitions',
  '/submit-your-work': '/submit',
  '/privacy-policy': '/privacy',
};
app.use(async (req, res, next) => {
  const path = req.path.replace(/\/+$/, '') || '/';
  if (REDIRECTS[path]) return res.redirect(301, REDIRECTS[path]);
  let m = path.match(/^\/product\/([^/]+)$/);
  if (m) return res.redirect(301, (await classSlugs()).has(m[1]) ? `/classes/${m[1]}` : `/shop/${m[1]}`);
  m = path.match(/^\/artist\/([^/]+)(?:\/page\/\d+)?$/);
  if (m) return res.redirect(301, `/artists/${m[1]}`);
  next();
});

app.get('/robots.txt', (_req, res) => {
  res.type('text/plain').send(`User-agent: *\nAllow: /\nDisallow: /api/\n\nSitemap: ${SITE_URL}/sitemap.xml\n`);
});

/** Sitemap built from live WordPress data, falling back to the bundled sample when WordPress is unreachable. */
app.get('/sitemap.xml', async (_req, res) => {
  const fetchJson = async <T>(path: string, fallback: T): Promise<T> => {
    try {
      const r = await fetch(WP_ORIGIN + path, { signal: AbortSignal.timeout(5000) });
      return r.ok ? ((await r.json()) as T) : fallback;
    } catch {
      return fallback;
    }
  };
  const [classes, artists, products] = await Promise.all([
    fetchJson<{ slug: string }[]>('/wp-json/6311/v1/classes', MOCK_CLASSES),
    fetchJson<{ slug: string }[]>('/wp-json/6311/v1/artists', MOCK_ARTISTS),
    fetchJson<{ slug: string }[]>('/wp-json/wc/store/v1/products?per_page=100', []),
  ]);
  const urls = [
    '/', '/classes', '/exhibitions', '/artists', '/shop', '/memberships', '/about', '/visit', '/submit',
    ...classes.map((c) => `/classes/${c.slug}`),
    ...artists.map((a) => `/artists/${a.slug}`),
    ...products.map((p) => `/shop/${p.slug}`),
  ];
  const body = urls.map((u) => `  <url><loc>${SITE_URL}${u}</loc></url>`).join('\n');
  res
    .type('application/xml')
    .set('Cache-Control', 'public, max-age=3600')
    .send(`<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${body}\n</urlset>\n`);
});

/**
 * Newsletter and contact form endpoints. They forward to a webhook you configure (for example a Zapier or
 * Make hook that adds the subscriber to MailPoet and emails info@), so no mail credentials live in this app.
 */
const forward = (hookEnv: string) => async (req: express.Request, res: express.Response) => {
  const body = req.body ?? {};
  if (body.company) return res.status(204).end(); // honeypot filled: a bot
  if (typeof body.email !== 'string' || !/^\S+@\S+\.\S+$/.test(body.email)) return res.status(400).json({ ok: false });
  const hook = process.env[hookEnv];
  if (!hook) {
    console.warn(`[${hookEnv}] not set; form submission accepted but not forwarded`);
    return res.status(202).json({ ok: true });
  }
  try {
    const r = await fetch(hook, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ ...body, company: undefined, source: SITE_URL }),
      signal: AbortSignal.timeout(8000),
    });
    return res.status(r.ok ? 200 : 502).json({ ok: r.ok });
  } catch {
    return res.status(502).json({ ok: false });
  }
};
app.post('/api/subscribe', forward('NEWSLETTER_WEBHOOK_URL'));
app.post('/api/contact', forward('CONTACT_WEBHOOK_URL'));

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
      // Let a CDN serve pages for 5 minutes and refresh in the background, so WordPress is hit rarely.
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
