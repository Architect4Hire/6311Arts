import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import { decodeExhibition, decodeExhibitionPage } from '../models/exhibition';
import { EXHIBITION, toExhibitionSummary } from '../models/exhibition.fixtures';
import { ExhibitionService } from './exhibition.service';

const BASE = '/api/v1/cms/exhibitions';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return {
    exhibitions: TestBed.inject(ExhibitionService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('ExhibitionService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { exhibitions, http } = setup();

    const page = firstValueFrom(
      exhibitions.list({ cursor: 'abc', limit: 50, status: 'Archived', search: 'boats' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('limit')).toBe('50');
    expect(request.request.params.get('status')).toBe('Archived');
    expect(request.request.params.get('search')).toBe('boats');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toExhibitionSummary(EXHIBITION)], nextCursor: null });

    expect(await page).toEqual({ items: [toExhibitionSummary(EXHIBITION)], nextCursor: null });
  });

  it('creates, then edits and runs a status command under the ETag it was given', async () => {
    const { exhibitions, http } = setup();

    const created = firstValueFrom(
      exhibitions.create({ title: 'Queue', startDate: '2026-05-16', endDate: '2026-07-04' }),
    );
    const post = http.expectOne(BASE);
    expect(post.request.method).toBe('POST');
    post.flush(EXHIBITION, { status: 201, statusText: 'Created' });
    expect(await created).toEqual(EXHIBITION);

    const edit = firstValueFrom(
      exhibitions.update(EXHIBITION.id, { artistIds: ['artist-2'] }, EXHIBITION.etag),
    );
    const patch = http.expectOne(`${BASE}/${EXHIBITION.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(EXHIBITION.etag);
    expect(patch.request.body).toEqual({ artistIds: ['artist-2'] });
    patch.flush(EXHIBITION);
    await edit;

    const archived = firstValueFrom(exhibitions.command(EXHIBITION.id, 'archive', EXHIBITION.etag));
    const command = http.expectOne(`${BASE}/${EXHIBITION.id}/archive`);
    expect(command.request.headers.get('If-Match')).toBe(EXHIBITION.etag);
    command.flush({ ...EXHIBITION, status: 'Archived' });
    expect((await archived).status).toBe('Archived');
  });

  it('refuses a body that is not an exhibition', async () => {
    const { exhibitions, http } = setup();

    const read = firstValueFrom(exhibitions.get(EXHIBITION.id));
    http.expectOne(`${BASE}/${EXHIBITION.id}`).flush({ id: EXHIBITION.id });

    await expect(read).rejects.toThrow('not an exhibition');
  });
});

describe('exhibition models', () => {
  it('decodes exactly the API shape, with or without a reception', () => {
    expect(decodeExhibition(EXHIBITION)).toEqual(EXHIBITION);
    expect(decodeExhibition({ ...EXHIBITION, reception: null })?.reception).toBeNull();
    expect(decodeExhibition({ ...EXHIBITION, reception: { startsAt: 'x' } })).toBeNull();
  });

  it('refuses a date that is not a calendar date, and a credit that is not an artist', () => {
    expect(decodeExhibition({ ...EXHIBITION, startDate: '2026-10-24T00:00:00Z' })).toBeNull();
    expect(decodeExhibition({ ...EXHIBITION, artists: [{ id: 'a', name: 'A' }] })).toBeNull();
    expect(decodeExhibition({ ...EXHIBITION, artists: null })).toBeNull();
  });

  it('decodes a page of rows and refuses one bad row', () => {
    expect(
      decodeExhibitionPage({ items: [toExhibitionSummary(EXHIBITION)], nextCursor: 'x' })?.items[0]
        .artistCount,
    ).toBe(2);
    expect(
      decodeExhibitionPage({
        items: [{ ...toExhibitionSummary(EXHIBITION), artistCount: 'two' }],
        nextCursor: null,
      }),
    ).toBeNull();
    expect(
      decodeExhibitionPage({
        items: [{ ...toExhibitionSummary(EXHIBITION), artistCount: '2' }],
        nextCursor: null,
      })?.items[0].artistCount,
    ).toBe(2);
    expect(
      decodeExhibitionPage({
        items: [{ ...toExhibitionSummary(EXHIBITION), artistCount: null }],
        nextCursor: null,
      }),
    ).toBeNull();
  });
});
