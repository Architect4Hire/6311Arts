import { FIXED_PAGES, buildSitemap } from './sitemap';

describe('buildSitemap', () => {
  it('lists the fixed pages, then every published record with its last change, each address once', () => {
    const xml = buildSitemap('https://6311arts.com', [
      { path: '/classes/intro-to-screen-printing', lastModified: '2026-10-21T12:00:00+00:00' },
      { path: '/memberships', lastModified: '2026-10-20T08:00:00+00:00' },
    ]);

    const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);
    expect(locs).toEqual([
      ...FIXED_PAGES.map((p) => `https://6311arts.com${p}`),
      'https://6311arts.com/classes/intro-to-screen-printing',
    ]);
    expect(xml).toContain(
      '<url><loc>https://6311arts.com/classes/intro-to-screen-printing</loc><lastmod>2026-10-21</lastmod></url>',
    );
    expect(xml.startsWith('<?xml version="1.0" encoding="UTF-8"?>')).toBe(true);
  });

  it('escapes what XML would read as markup', () => {
    expect(
      buildSitemap('https://6311arts.com', [
        { path: "/artists/o'neil&co", lastModified: '2026-10-21' },
      ]),
    ).toContain('<loc>https://6311arts.com/artists/o&apos;neil&amp;co</loc>');
  });
});
