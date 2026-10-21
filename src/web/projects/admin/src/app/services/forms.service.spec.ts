import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';
import {
  decodeContactMessage,
  decodeContactMessageList,
  decodeNewsletterList,
  decodeNewsletterSubscriber,
  decodeSubmission,
  decodeSubmissionList,
  fileNameOf,
  safeWebLink,
} from '../models/forms';
import {
  CONTACT_MESSAGE,
  SUBMISSION,
  SUBSCRIBER,
  toSubmissionSummary,
} from '../models/forms.fixtures';
import { FormsService } from './forms.service';

const BASE = '/api/v1/cms/forms';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { forms: TestBed.inject(FormsService), http: TestBed.inject(HttpTestingController) };
}

describe('FormsService', () => {
  it('lists submissions with the status filter and cursor, and shows failures in place', async () => {
    const { forms, http } = setup();

    const list = firstValueFrom(forms.listSubmissions({ cursor: 'abc', limit: 50, status: 'New' }));
    const request = http.expectOne((r) => r.url === `${BASE}/submissions`);
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('limit')).toBe('50');
    expect(request.request.params.get('status')).toBe('New');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush({ items: [toSubmissionSummary(SUBMISSION)], nextCursor: 'next' });

    expect(await list).toEqual({ items: [toSubmissionSummary(SUBMISSION)], nextCursor: 'next' });
  });

  it('asks for messages by handled, sending false as false rather than leaving it out', async () => {
    const { forms, http } = setup();

    const unhandled = firstValueFrom(forms.listMessages({ handled: false }));
    const request = http.expectOne((r) => r.url === `${BASE}/messages`);
    expect(request.request.params.get('handled')).toBe('false');
    request.flush({ items: [CONTACT_MESSAGE], nextCursor: null });
    expect((await unhandled).items).toEqual([CONTACT_MESSAGE]);

    const all = firstValueFrom(forms.listMessages());
    const plain = http.expectOne((r) => r.url === `${BASE}/messages`);
    expect(plain.request.params.has('handled')).toBe(false);
    plain.flush({ items: [], nextCursor: null });
    await all;
  });

  it('searches the newsletter list', async () => {
    const { forms, http } = setup();

    const list = firstValueFrom(forms.listSubscribers({ search: 'reader' }));
    const request = http.expectOne((r) => r.url === `${BASE}/newsletter`);
    expect(request.request.params.get('search')).toBe('reader');
    request.flush({ items: [SUBSCRIBER], nextCursor: null });
    expect((await list).items).toEqual([SUBSCRIBER]);
  });

  it('sends a submission review and a message mark under the ETag it was given', async () => {
    const { forms, http } = setup();

    const review = firstValueFrom(
      forms.updateSubmission(
        SUBMISSION.id,
        { status: 'Reviewed', notes: 'Call her.' },
        SUBMISSION.etag,
      ),
    );
    const patch = http.expectOne(`${BASE}/submissions/${SUBMISSION.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(SUBMISSION.etag);
    expect(patch.request.body).toEqual({ status: 'Reviewed', notes: 'Call her.' });
    patch.flush({ ...SUBMISSION, status: 'Reviewed', notes: 'Call her.' });
    expect((await review).status).toBe('Reviewed');

    const mark = firstValueFrom(
      forms.updateMessage(CONTACT_MESSAGE.id, { handled: true }, CONTACT_MESSAGE.etag),
    );
    const markRequest = http.expectOne(`${BASE}/messages/${CONTACT_MESSAGE.id}`);
    expect(markRequest.request.method).toBe('PATCH');
    expect(markRequest.request.headers.get('If-Match')).toBe(CONTACT_MESSAGE.etag);
    expect(markRequest.request.body).toEqual({ handled: true });
    markRequest.flush({ ...CONTACT_MESSAGE, handled: true });
    expect((await mark).handled).toBe(true);
  });

  it('removes an address with a bodiless DELETE', async () => {
    const { forms, http } = setup();

    const removed = firstValueFrom(forms.removeSubscriber(SUBSCRIBER.id));
    const request = http.expectOne(`${BASE}/newsletter/${SUBSCRIBER.id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await removed;
  });

  it('exports the list as a file named by the API', async () => {
    const { forms, http } = setup();

    const exported = firstValueFrom(forms.exportSubscribers());
    const request = http.expectOne(`${BASE}/newsletter/export`);
    expect(request.request.responseType).toBe('blob');
    expect(request.request.context.get(SUPPRESS_PROBLEM_TOAST)).toBe(true);
    request.flush(new Blob(['email\r\nreader@example.com\r\n'], { type: 'text/csv' }), {
      headers: {
        'Content-Disposition':
          "attachment; filename=6311arts-newsletter-2026-10-21.csv; filename*=UTF-8''6311arts-newsletter-2026-10-21.csv",
      },
    });

    const file = await exported;
    expect(file.fileName).toBe('6311arts-newsletter-2026-10-21.csv');
    expect(await file.blob.text()).toContain('reader@example.com');
  });

  it('reads an export refusal back into ProblemDetails', async () => {
    const { forms, http } = setup();

    const exported = firstValueFrom(forms.exportSubscribers());
    http.expectOne(`${BASE}/newsletter/export`).flush(
      new Blob([JSON.stringify({ code: 'access.forbidden', correlationId: 'abc' })], {
        type: 'application/problem+json',
      }),
      { status: 403, statusText: 'Forbidden' },
    );

    const error = await exported.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(403);
    expect((error as HttpErrorResponse).error).toEqual({
      code: 'access.forbidden',
      correlationId: 'abc',
    });
  });

  it('refuses a body that is not a submission or a message', async () => {
    const { forms, http } = setup();

    const read = firstValueFrom(forms.getSubmission(SUBMISSION.id));
    http.expectOne(`${BASE}/submissions/${SUBMISSION.id}`).flush({ id: SUBMISSION.id });
    await expect(read).rejects.toThrow('not a submission');

    const message = firstValueFrom(forms.getMessage(CONTACT_MESSAGE.id));
    http
      .expectOne(`${BASE}/messages/${CONTACT_MESSAGE.id}`)
      .flush({ ...CONTACT_MESSAGE, handled: 'yes' });
    await expect(message).rejects.toThrow('not a contact message');
  });
});

describe('forms models', () => {
  it('decodes exactly the API shapes', () => {
    expect(decodeSubmission(SUBMISSION)).toEqual(SUBMISSION);
    expect(decodeContactMessage(CONTACT_MESSAGE)).toEqual(CONTACT_MESSAGE);
    expect(decodeNewsletterSubscriber(SUBSCRIBER)).toEqual(SUBSCRIBER);
    expect(
      decodeSubmissionList({ items: [toSubmissionSummary(SUBMISSION)], nextCursor: 'x' })
        ?.nextCursor,
    ).toBe('x');
  });

  it.each([
    ['an unknown status', { ...SUBMISSION, status: 'Maybe' }],
    ['wants that are not words', { ...SUBMISSION, wants: [1] }],
    [
      'a picture without its address',
      { ...SUBMISSION, images: [{ ...SUBMISSION.images[0], originalUrl: null }] },
    ],
    [
      'answers missing the newsletter flag',
      { ...SUBMISSION, answers: { ...SUBMISSION.answers, newsletter: null } },
    ],
    [
      'a teach block with a fractional size',
      {
        ...SUBMISSION,
        answers: {
          ...SUBMISSION.answers,
          teach: { ...SUBMISSION.answers.teach, maxStudents: 2.5 },
        },
      },
    ],
  ])('refuses a submission with %s', (_, body) => {
    expect(decodeSubmission(body)).toBeNull();
  });

  it('refuses a page with one bad row, and an unknown newsletter source', () => {
    expect(decodeContactMessageList({ items: [CONTACT_MESSAGE, {}], nextCursor: null })).toBeNull();
    expect(
      decodeNewsletterList({ items: [{ ...SUBSCRIBER, source: 'Import' }], nextCursor: null }),
    ).toBeNull();
    expect(decodeSubmissionList({ items: [], nextCursor: 1 })).toBeNull();
  });

  it.each([
    ['https://rosaortiz.example', 'https://rosaortiz.example/'],
    ['http://old.example/page', 'http://old.example/page'],
    ['rosaortiz.example', null],
    ['javascript:alert(1)', null],
    ['/admin', null],
    ['', null],
  ])('treats %j as a link to %j', (value, href) => {
    expect(safeWebLink(value)).toBe(href);
  });

  it('reads the download name from Content-Disposition, and strips path characters', () => {
    expect(fileNameOf('attachment; filename="list.csv"')).toBe('list.csv');
    expect(fileNameOf("attachment; filename*=UTF-8''na%C3%AFve.csv")).toBe('naïve.csv');
    expect(fileNameOf('attachment; filename="../../etc/passwd"')).toBe('....etcpasswd');
    expect(fileNameOf(null)).toBeNull();
  });
});
