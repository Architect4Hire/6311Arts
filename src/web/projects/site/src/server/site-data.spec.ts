import { ROUTING_RETRY_MS, ROUTING_TTL_MS, createSiteData } from './site-data';

const REDIRECTS = [{ fromPath: '/gallery', toPath: '/exhibitions', statusCode: 301 }];
const SITEMAP = [
  { path: '/classes/screen-printing', lastModified: '2026-10-21T00:00:00+00:00' },
  { path: '/shop/copper-cuff', lastModified: '2026-10-21T00:00:00+00:00' },
];

/** A fetch that answers from a table, counting calls; a path set to `null` fails. */
function fakeFetch(table: Record<string, unknown>) {
  const calls: string[] = [];
  const impl = async (url: string) => {
    calls.push(url);
    const path = new URL(url).pathname;
    if (table[path] === null || !(path in table)) throw new Error('unreachable');
    return { ok: true, json: async () => table[path] };
  };
  return { impl, calls };
}

describe('createSiteData', () => {
  afterEach(() => vi.restoreAllMocks());

  it('reads the redirects and class slugs through the gateway, and the sitemap when asked', async () => {
    const fetch = fakeFetch({
      '/api/v1/site/redirects': REDIRECTS,
      '/api/v1/site/sitemap': SITEMAP,
    });
    const data = createSiteData('https://gateway:7443/', fetch.impl);

    const routing = await data.routing();

    expect(routing.redirects).toEqual(REDIRECTS);
    expect([...routing.classSlugs]).toEqual(['screen-printing']);
    expect(await data.sitemap()).toEqual(SITEMAP);
    expect(fetch.calls.every((url) => url.startsWith('https://gateway:7443/api/v1/site/'))).toBe(
      true,
    );
  });

  it('answers from what it has and refreshes behind the request once a minute', async () => {
    let now = 0;
    const fetch = fakeFetch({
      '/api/v1/site/redirects': REDIRECTS,
      '/api/v1/site/sitemap': SITEMAP,
    });
    const data = createSiteData('https://gateway', fetch.impl, () => now);

    await data.routing();
    await data.routing();
    expect(fetch.calls).toHaveLength(2);

    now = ROUTING_TTL_MS;
    await data.routing();
    await new Promise((resolve) => setTimeout(resolve));
    expect(fetch.calls).toHaveLength(4);
  });

  it('keeps the last good redirects when the API goes away', async () => {
    let now = 0;
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const table: Record<string, unknown> = {
      '/api/v1/site/redirects': REDIRECTS,
      '/api/v1/site/sitemap': SITEMAP,
    };
    const data = createSiteData('https://gateway', fakeFetch(table).impl, () => now);
    await data.routing();

    table['/api/v1/site/redirects'] = null;
    table['/api/v1/site/sitemap'] = null;
    now = ROUTING_TTL_MS;
    await data.routing();
    await new Promise((resolve) => setTimeout(resolve));

    const routing = await data.routing();
    expect(routing.redirects).toEqual(REDIRECTS);
    expect([...routing.classSlugs]).toEqual(['screen-printing']);
    expect(await data.sitemap()).toBeNull();
  });

  it('asks again within seconds when the first read failed, as when the API is still starting', async () => {
    let now = 0;
    const table: Record<string, unknown> = {
      '/api/v1/site/redirects': null,
      '/api/v1/site/sitemap': null,
    };
    const data = createSiteData('https://gateway', fakeFetch(table).impl, () => now);
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    expect((await data.routing()).classSlugs.size).toBe(0);

    table['/api/v1/site/redirects'] = REDIRECTS;
    table['/api/v1/site/sitemap'] = SITEMAP;
    now = ROUTING_RETRY_MS;
    await data.routing();
    await new Promise((resolve) => setTimeout(resolve));

    expect([...(await data.routing()).classSlugs]).toEqual(['screen-printing']);
    expect(ROUTING_RETRY_MS).toBeLessThan(ROUTING_TTL_MS);
  });

  it('has nothing to read without a gateway, and says so rather than guessing', async () => {
    const data = createSiteData(null);
    expect(await data.routing()).toEqual({ redirects: [], classSlugs: new Set() });
    expect(await data.sitemap()).toBeNull();
  });
});
