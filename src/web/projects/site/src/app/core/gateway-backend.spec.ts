import { FetchBackend, HttpRequest } from '@angular/common/http';
import { REQUEST } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY, firstValueFrom } from 'rxjs';
import { GatewayBackend, SITE_API_ORIGIN } from './gateway-backend';

/** The URL each request reached fetch with. */
function setup(origin: string | null, page = 'http://localhost:4200/shop') {
  const sent: string[] = [];
  vi.spyOn(FetchBackend.prototype, 'handle').mockImplementation((req: HttpRequest<unknown>) => {
    sent.push(req.urlWithParams);
    return EMPTY;
  });
  TestBed.configureTestingModule({
    providers: [
      GatewayBackend,
      { provide: SITE_API_ORIGIN, useValue: origin },
      { provide: REQUEST, useValue: new Request(page) },
    ],
  });
  const backend = TestBed.inject(GatewayBackend);
  const send = (url: string) =>
    backend.handle(new HttpRequest('GET', url)).subscribe({ error: () => undefined });
  return { backend, sent, send };
}

describe('GatewayBackend', () => {
  afterEach(() => vi.restoreAllMocks());

  it('sends a relative site API read to the gateway', () => {
    const { sent, send } = setup('https://localhost:7443/');
    send('/api/v1/site/shop?category=prints');
    expect(sent).toEqual(['https://localhost:7443/api/v1/site/shop?category=prints']);
  });

  it('re-targets a read the server platform already resolved against the page being rendered', () => {
    const { sent, send } = setup('https://localhost:7443');
    send('http://localhost:4200/api/v1/site/classes');
    expect(sent).toEqual(['https://localhost:7443/api/v1/site/classes']);
  });

  it('passes every other request through untouched', () => {
    const { sent, send } = setup('https://localhost:7443');
    send('/bff/session');
    send('https://example.org/api/v1/site/classes');
    send('http://localhost:4200/assets/logo.webp');
    expect(sent).toEqual([
      '/bff/session',
      'https://example.org/api/v1/site/classes',
      'http://localhost:4200/assets/logo.webp',
    ]);
  });

  it('fails a site API read when there is no gateway to ask, without sending it anywhere', async () => {
    const { backend, sent } = setup(null);
    await expect(
      firstValueFrom(backend.handle(new HttpRequest('GET', '/api/v1/site/classes'))),
    ).rejects.toThrow('SITE_API_ORIGIN');
    expect(sent).toEqual([]);
  });

  it('is still the fetch backend, so Angular’s server warning about XHR stays quiet', () => {
    expect(setup('https://localhost:7443').backend).toBeInstanceOf(FetchBackend);
  });
});
