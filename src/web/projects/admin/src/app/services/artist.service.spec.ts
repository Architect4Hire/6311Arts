import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  decodeArtist,
  decodeArtistPage,
  decodeArtistProblem,
  isSlug,
  slugFromName,
} from '../models/artist';
import { ARTIST, toArtistSummary } from '../models/artist.fixtures';
import { ArtistService } from './artist.service';

const BASE = '/api/v1/cms/artists';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { artists: TestBed.inject(ArtistService), http: TestBed.inject(HttpTestingController) };
}

describe('ArtistService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { artists, http } = setup();

    const page = firstValueFrom(
      artists.list({ cursor: 'abc', limit: 50, status: 'Published', search: 'haase' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('limit')).toBe('50');
    expect(request.request.params.get('status')).toBe('Published');
    expect(request.request.params.get('search')).toBe('haase');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toArtistSummary(ARTIST)], nextCursor: null });

    expect(await page).toEqual({ items: [toArtistSummary(ARTIST)], nextCursor: null });
  });

  it('sends the sort with the list, and leaves it off when there is none (ADR 0062)', async () => {
    const { artists, http } = setup();

    void firstValueFrom(artists.list({ sort: '-updated' }));
    expect(http.expectOne((r) => r.url === BASE).request.params.get('sort')).toBe('-updated');

    void firstValueFrom(artists.list());
    expect(http.expectOne((r) => r.url === BASE).request.params.has('sort')).toBe(false);

    void firstValueFrom(artists.listDeleted({ search: 'haase' }));
    expect(http.expectOne((r) => r.url === `${BASE}/deleted`).request.params.has('sort')).toBe(
      false,
    );
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { artists, http } = setup();

    const edit = firstValueFrom(artists.update(ARTIST.id, { bio: '' }, ARTIST.etag));
    const patch = http.expectOne(`${BASE}/${ARTIST.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(ARTIST.etag);
    expect(patch.request.body).toEqual({ bio: '' });
    patch.flush(ARTIST);
    expect(await edit).toEqual(ARTIST);

    const published = firstValueFrom(artists.command(ARTIST.id, 'publish', ARTIST.etag));
    const command = http.expectOne(`${BASE}/${ARTIST.id}/publish`);
    expect(command.request.method).toBe('POST');
    expect(command.request.headers.get('If-Match')).toBe(ARTIST.etag);
    command.flush({ ...ARTIST, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('refuses a body that is not an artist', async () => {
    const { artists, http } = setup();

    const read = firstValueFrom(artists.get(ARTIST.id));
    http.expectOne(`${BASE}/${ARTIST.id}`).flush({ id: ARTIST.id });

    await expect(read).rejects.toThrow('not a artist');
  });
});

describe('artist models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodeArtist(ARTIST)).toEqual(ARTIST);
    expect(decodeArtist({ ...ARTIST, status: 'Live' })).toBeNull();
    expect(decodeArtist({ ...ARTIST, slugLocked: 'no' })).toBeNull();
    expect(
      decodeArtistPage({ items: [toArtistSummary(ARTIST)], nextCursor: 'x' })?.nextCursor,
    ).toBe('x');
    expect(decodeArtistPage({ items: [{}], nextCursor: null })).toBeNull();
  });

  it('reads the int64 legacy id as a number or a numeric string, and nothing else', () => {
    expect(decodeArtist({ ...ARTIST, legacyWordPressId: 42 })?.legacyWordPressId).toBe(42);
    expect(decodeArtist({ ...ARTIST, legacyWordPressId: '42' })?.legacyWordPressId).toBe(42);
    expect(decodeArtist({ ...ARTIST, legacyWordPressId: '4x' })).toBeNull();
    expect(decodeArtist({ ...ARTIST, legacyWordPressId: 1.5 })).toBeNull();
  });

  it('reads field errors from a refusal', () => {
    expect(
      decodeArtistProblem(
        { code: 'cms.slug_taken', detail: 'Taken.', errors: { slug: ['“jane” is taken.'] } },
        409,
      ),
    ).toEqual({
      status: 409,
      code: 'cms.slug_taken',
      detail: 'Taken.',
      fieldErrors: { slug: ['“jane” is taken.'] },
      correlationId: null,
    });
  });

  it.each([
    ['Candice Johnson', 'candice-johnson'],
    ["Marco Da'Vila", 'marco-da-vila'],
    ['  Socorro Muciño ', 'socorro-mucino'],
    ['—', ''],
  ])('makes the slug the API would from %j', (name, slug) => {
    expect(slugFromName(name)).toBe(slug);
  });

  it('knows a slug when it sees one', () => {
    expect(isSlug('jane-doe')).toBe(true);
    expect(isSlug('Jane-Doe')).toBe(false);
    expect(isSlug('jane--doe')).toBe(false);
    expect(isSlug('')).toBe(false);
    expect(isSlug('a'.repeat(121))).toBe(false);
  });
});
