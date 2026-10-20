import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  decodeClassOffering,
  decodeClassPage,
  decodeClassSummary,
  isSku,
  normalizeSku,
  suggestedSku,
} from '../models/class';
import { CLASS_OFFERING, SYNCED_CLASS, toClassSummary } from '../models/class.fixtures';
import { ClassService } from './class.service';

const BASE = '/api/v1/cms/classes';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { classes: TestBed.inject(ClassService), http: TestBed.inject(HttpTestingController) };
}

describe('ClassService', () => {
  it('lists with the filters as query parameters, and shows failures in place', async () => {
    const { classes, http } = setup();

    const list = firstValueFrom(
      classes.list({ cursor: 'abc', limit: 50, status: 'Published', search: 'print' }),
    );
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('status')).toBe('Published');
    expect(request.request.params.get('search')).toBe('print');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toClassSummary(CLASS_OFFERING)], nextCursor: null });

    expect(await list).toEqual({ items: [toClassSummary(CLASS_OFFERING)], nextCursor: null });
  });

  it('sends an edit and a status command under the ETag it was given', async () => {
    const { classes, http } = setup();

    const edit = firstValueFrom(
      classes.update(CLASS_OFFERING.id, { summary: '' }, CLASS_OFFERING.etag),
    );
    const patch = http.expectOne(`${BASE}/${CLASS_OFFERING.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(CLASS_OFFERING.etag);
    expect(patch.request.body).toEqual({ summary: '' });
    patch.flush(CLASS_OFFERING);
    await edit;

    const published = firstValueFrom(
      classes.command(CLASS_OFFERING.id, 'publish', CLASS_OFFERING.etag),
    );
    const command = http.expectOne(`${BASE}/${CLASS_OFFERING.id}/publish`);
    expect(command.request.headers.get('If-Match')).toBe(CLASS_OFFERING.etag);
    expect(command.request.body).toBeNull();
    command.flush({ ...CLASS_OFFERING, status: 'Published' });
    expect((await published).status).toBe('Published');
  });

  it('sends the schedule command with its time in the body', async () => {
    const { classes, http } = setup();

    const scheduled = firstValueFrom(
      classes.schedule(CLASS_OFFERING.id, '2026-12-24T17:00:00+00:00', CLASS_OFFERING.etag),
    );
    const request = http.expectOne(`${BASE}/${CLASS_OFFERING.id}/schedule`);
    expect(request.request.body).toEqual({ scheduledFor: '2026-12-24T17:00:00+00:00' });
    request.flush({
      ...CLASS_OFFERING,
      status: 'Scheduled',
      scheduledFor: '2026-12-24T17:00:00+00:00',
    });

    expect((await scheduled).scheduledFor).toBe('2026-12-24T17:00:00+00:00');
  });

  it('refuses a body that is not a class', async () => {
    const { classes, http } = setup();

    const read = firstValueFrom(classes.get(CLASS_OFFERING.id));
    http.expectOne(`${BASE}/${CLASS_OFFERING.id}`).flush({ id: CLASS_OFFERING.id });

    await expect(read).rejects.toThrow('not a class');
  });
});

describe('class models', () => {
  it('decodes exactly the API shape', () => {
    expect(decodeClassOffering(CLASS_OFFERING)).toEqual(CLASS_OFFERING);
    expect(decodeClassOffering(SYNCED_CLASS)).toEqual(SYNCED_CLASS);
    expect(decodeClassOffering({ ...CLASS_OFFERING, body: null })?.body).toBeNull();
    expect(decodeClassOffering({ ...CLASS_OFFERING, startingPriceCents: null })).not.toBeNull();
    expect(decodeClassOffering({ ...CLASS_OFFERING, currency: undefined })).toBeNull();
    expect(decodeClassOffering({ ...CLASS_OFFERING, sessions: [{}] })).toBeNull();
    expect(decodeClassOffering({ ...CLASS_OFFERING, imageMediaIds: [1] })).toBeNull();
    expect(decodeClassOffering({ ...CLASS_OFFERING, startingPriceEditable: 'yes' })).toBeNull();
  });

  it('takes an integer the schema allows as a string, and refuses a half one', () => {
    expect(decodeClassOffering({ ...CLASS_OFFERING, offeringNumber: '4' })?.offeringNumber).toBe(4);
    expect(decodeClassOffering({ ...CLASS_OFFERING, offeringNumber: '4.5' })).toBeNull();
    expect(decodeClassSummary({ ...toClassSummary(CLASS_OFFERING), sessionCount: '2' })?.sessionCount).toBe(2);
    expect(decodeClassSummary({ ...toClassSummary(CLASS_OFFERING), sessionCount: null })).toBeNull();
  });

  it('decodes a page and refuses one whose rows are not summaries', () => {
    expect(
      decodeClassPage({ items: [toClassSummary(CLASS_OFFERING)], nextCursor: 'x' })?.nextCursor,
    ).toBe('x');
    expect(decodeClassPage({ items: [{}], nextCursor: null })).toBeNull();
    expect(decodeClassPage({ items: 'none', nextCursor: null })).toBeNull();
  });

  it('matches the API on what a SKU looks like and how it is written', () => {
    expect(normalizeSku('  cls-4-1 ')).toBe('CLS-4-1');
    expect(suggestedSku(4, 2)).toBe('CLS-4-2');
    expect(isSku('CLS-4-1')).toBe(true);
    expect(isSku('WORKSHOP.2B')).toBe(true);
    expect(isSku('CLS 4 1')).toBe(false);
    expect(isSku('CLS--4')).toBe(false);
    expect(isSku('')).toBe(false);
    expect(isSku('A'.repeat(65))).toBe(false);
  });
});
