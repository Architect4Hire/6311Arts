import {
  SPIKE_REDIRECTS,
  SiteRedirect,
  normalizeAddress,
  resolveRedirect,
} from './legacy-redirects';

const CLASSES = new Set(['intro-to-screen-printing']);

describe('normalizeAddress', () => {
  it('reads an address as the Redirects module stores one: lower case, no trailing slash, query kept', () => {
    expect(normalizeAddress('/Gallery/')).toEqual({ path: '/gallery', withQuery: '/gallery' });
    expect(normalizeAddress('/?p=123')).toEqual({ path: '/', withQuery: '/?p=123' });
    expect(normalizeAddress('/events?')).toEqual({ path: '/events', withQuery: '/events' });
    expect(normalizeAddress('/caf%C3%A9')).toEqual({ path: '/café', withQuery: '/café' });
  });
});

describe('resolveRedirect', () => {
  it('sends every address in the spike’s map where it always went, when the API has nothing', () => {
    for (const [from, to] of Object.entries(SPIKE_REDIRECTS)) {
      expect(resolveRedirect(from, [], CLASSES)).toEqual({ location: to, status: 301 });
      expect(resolveRedirect(from.toUpperCase() + '/', [], CLASSES)).toEqual({
        location: to,
        status: 301,
      });
    }
  });

  it('sends a WooCommerce product to its class when it is one, and to the shop otherwise', () => {
    expect(resolveRedirect('/product/intro-to-screen-printing/', [], CLASSES)).toEqual({
      location: '/classes/intro-to-screen-printing',
      status: 301,
    });
    expect(resolveRedirect('/product/copper-cuff', [], CLASSES)).toEqual({
      location: '/shop/copper-cuff',
      status: 301,
    });
  });

  it('sends an artist archive, any page of it, to the artist', () => {
    expect(resolveRedirect('/artist/jane-doe', [], CLASSES)).toEqual({
      location: '/artists/jane-doe',
      status: 301,
    });
    expect(resolveRedirect('/artist/jane-doe/page/3/', [], CLASSES)).toEqual({
      location: '/artists/jane-doe',
      status: 301,
    });
  });

  it('lets the CMS’s redirects answer first, with their own status, the query first', () => {
    const redirects: SiteRedirect[] = [
      { fromPath: '/gallery', toPath: '/exhibitions/boats-and-pianos', statusCode: 302 },
      { fromPath: '/?p=123', toPath: '/classes', statusCode: 301 },
      { fromPath: '/old-page', toPath: 'https://example.org/elsewhere', statusCode: 301 },
    ];

    expect(resolveRedirect('/gallery', redirects, CLASSES)).toEqual({
      location: '/exhibitions/boats-and-pianos',
      status: 302,
    });
    expect(resolveRedirect('/?p=123', redirects, CLASSES)).toEqual({
      location: '/classes',
      status: 301,
    });
    expect(resolveRedirect('/old-page?utm=x', redirects, CLASSES)).toEqual({
      location: 'https://example.org/elsewhere',
      status: 301,
    });
  });

  it('renders anything else, and never answers a redirect back to the same address', () => {
    expect(resolveRedirect('/classes', [], CLASSES)).toBeNull();
    expect(resolveRedirect('/', [], CLASSES)).toBeNull();
    expect(
      resolveRedirect('/loop', [{ fromPath: '/loop', toPath: '/loop', statusCode: 301 }], CLASSES),
    ).toBeNull();
  });
});
