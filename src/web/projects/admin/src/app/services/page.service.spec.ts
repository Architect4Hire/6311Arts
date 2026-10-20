import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import { isAllowedLink, isMediaImageSrc, mediaImageSrc } from '../models/html';
import { decodePage, decodePageList } from '../models/page';
import { PAGE, toPageSummary } from '../models/page.fixtures';
import { PageService } from './page.service';

const BASE = '/api/v1/cms/pages';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { pages: TestBed.inject(PageService), http: TestBed.inject(HttpTestingController) };
}

describe('PageService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { pages, http } = setup();

    const list = firstValueFrom(
      pages.list({ cursor: 'abc', limit: 50, status: 'Draft', search: 'priv' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('status')).toBe('Draft');
    expect(request.request.params.get('search')).toBe('priv');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toPageSummary(PAGE)], nextCursor: null });

    expect(await list).toEqual({ items: [toPageSummary(PAGE)], nextCursor: null });
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { pages, http } = setup();

    const edit = firstValueFrom(pages.update(PAGE.id, { body: '' }, PAGE.etag));
    const patch = http.expectOne(`${BASE}/${PAGE.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(PAGE.etag);
    expect(patch.request.body).toEqual({ body: '' });
    patch.flush(PAGE);
    await edit;

    const published = firstValueFrom(pages.command(PAGE.id, 'publish', PAGE.etag));
    const command = http.expectOne(`${BASE}/${PAGE.id}/publish`);
    expect(command.request.headers.get('If-Match')).toBe(PAGE.etag);
    command.flush({ ...PAGE, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('refuses a body that is not a page', async () => {
    const { pages, http } = setup();

    const read = firstValueFrom(pages.get(PAGE.id));
    http.expectOne(`${BASE}/${PAGE.id}`).flush({ id: PAGE.id });

    await expect(read).rejects.toThrow('not a page');
  });
});

describe('page models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodePage(PAGE)).toEqual(PAGE);
    expect(decodePage({ ...PAGE, body: null })?.body).toBeNull();
    expect(decodePage({ ...PAGE, imageMediaIds: [1] })).toBeNull();
    expect(decodePage({ ...PAGE, imageMediaIds: undefined })).toBeNull();
    expect(decodePageList({ items: [toPageSummary(PAGE)], nextCursor: 'x' })?.nextCursor).toBe('x');
    expect(decodePageList({ items: [{}], nextCursor: null })).toBeNull();
  });
});

describe('the HTML allow-list, as the editor applies it', () => {
  it.each([
    ['/visit', true],
    ['/', true],
    ['#hours', true],
    ['https://6311arts.com/', true],
    ['mailto:info@6311arts.com', true],
    ['tel:+17735550100', true],
    ['javascript:alert(1)', false],
    ['//evil.example', false],
    ['/\\evil.example', false],
    ['#', false],
    ['visit', false],
    ['ftp://files.example/', false],
    ['https://a.example/ b', false],
  ])('treats %j as allowed: %s', (href, allowed) => {
    expect(isAllowedLink(href)).toBe(allowed);
  });

  it('writes and recognises library image sources only', () => {
    const src = mediaImageSrc('0199f0a0-0000-7000-8000-0000000000aa');
    expect(src).toBe('/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp');
    expect(isMediaImageSrc(src)).toBe(true);
    expect(isMediaImageSrc(`${src}?v=0123456789ab`)).toBe(true);
    expect(isMediaImageSrc('https://elsewhere.example/a.jpg')).toBe(false);
    expect(isMediaImageSrc('/media/not-a-guid/800.webp')).toBe(false);
  });
});
