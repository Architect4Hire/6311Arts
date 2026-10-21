import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { vi } from 'vitest';
import { BrowserDownload } from '../../core/browser/browser-download';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { ContactMessage, Submission } from '../../models/forms';
import {
  CONTACT_MESSAGE,
  SUBMISSION,
  SUBSCRIBER,
  toSubmissionSummary,
} from '../../models/forms.fixtures';
import { MessageDetail } from './message-detail';
import { MessageList } from './message-list';
import { NewsletterList } from './newsletter-list';
import { SubmissionDetail } from './submission-detail';
import { SubmissionList } from './submission-list';

const BASE = '/api/v1/cms/forms';
const SUBMISSIONS = `${BASE}/submissions`;
const MESSAGES = `${BASE}/messages`;
const NEWSLETTER = `${BASE}/newsletter`;

async function configure(roles: string[] = ['Editor']) {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
  return { http, confirm: TestBed.inject(ConfirmService) };
}

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label);
}

function rows(root: HTMLElement): string[] {
  return [...root.querySelectorAll('tbody tr')].map((row) => text(row));
}

async function openSubmission(submission: Submission, roles?: string[]) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(SubmissionDetail);
  fixture.componentRef.setInput('id', submission.id);
  fixture.detectChanges();
  context.http.expectOne(`${SUBMISSIONS}/${submission.id}`).flush(submission);
  await settle(fixture);
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

async function openMessage(message: ContactMessage) {
  const context = await configure();
  const fixture = TestBed.createComponent(MessageDetail);
  fixture.componentRef.setInput('id', message.id);
  fixture.detectChanges();
  context.http.expectOne(`${MESSAGES}/${message.id}`).flush(message);
  await settle(fixture);
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('SubmissionList', () => {
  it('lists each submission with its wants as words and its status as a word', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(SubmissionList);
    fixture.detectChanges();

    const request = http.expectOne((r) => r.url === SUBMISSIONS);
    expect(request.request.params.has('status')).toBe(false);
    request.flush({
      items: [
        toSubmissionSummary(SUBMISSION),
        {
          ...toSubmissionSummary(SUBMISSION),
          id: 'b',
          name: 'Lee Park',
          wants: ['exhibit', 'hello'],
          status: 'Accepted',
          imageCount: 0,
        },
      ],
      nextCursor: null,
    });
    await settle(fixture);

    const [first, second] = rows(fixture.nativeElement);
    expect(first).toContain('Rosa Ortiz');
    expect(first).toContain('rosa@example.com');
    expect(first).toContain('Sell, Teach');
    expect(first).toContain('New');
    expect(first).toContain('2');
    expect(second).toContain('Exhibit, Say hello');
    expect(second).toContain('Accepted');
    const link = (fixture.nativeElement as HTMLElement).querySelector('tbody a');
    expect(link?.getAttribute('href')).toBe(`/forms/submissions/${SUBMISSION.id}`);
  });

  it('filters by status and loads more under the cursor', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(SubmissionList);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === SUBMISSIONS)
      .flush({ items: [toSubmissionSummary(SUBMISSION)], nextCursor: null });
    await settle(fixture);
    const root = fixture.nativeElement as HTMLElement;

    button(root, 'Reviewed')!.click();
    await settle(fixture);
    const filtered = http.expectOne((r) => r.url === SUBMISSIONS);
    expect(filtered.request.params.get('status')).toBe('Reviewed');
    filtered.flush({
      items: [{ ...toSubmissionSummary(SUBMISSION), status: 'Reviewed' }],
      nextCursor: 'page-2',
    });
    await settle(fixture);
    expect(button(root, 'Reviewed')!.getAttribute('aria-pressed')).toBe('true');

    button(root, 'Load more')!.click();
    const more = http.expectOne((r) => r.url === SUBMISSIONS);
    expect(more.request.params.get('cursor')).toBe('page-2');
    expect(more.request.params.get('status')).toBe('Reviewed');
    more.flush({
      items: [{ ...toSubmissionSummary(SUBMISSION), id: 'c', name: 'Kim Wu', status: 'Reviewed' }],
      nextCursor: null,
    });
    await settle(fixture);

    expect(rows(root)).toHaveLength(2);
    expect(button(root, 'Load more')).toBeUndefined();
  });

  it('offers a retry when the inbox cannot be loaded, and an empty state when it is empty', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(SubmissionList);
    fixture.detectChanges();
    expect(text(fixture.nativeElement)).toContain('Loading submissions…');

    http
      .expectOne((r) => r.url === SUBMISSIONS)
      .flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('The submissions couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === SUBMISSIONS).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('No submissions yet');
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('SubmissionDetail', () => {
  it('shows every answer grouped, as text, with the visitor’s links opening safely', async () => {
    const hostile: Submission = {
      ...SUBMISSION,
      answers: {
        ...SUBMISSION.answers,
        bio: '<img src=x onerror="alert(1)">Potter',
        portfolio: 'javascript:alert(1)',
      },
    };
    const { root } = await openSubmission(hostile);

    const headings = [...root.querySelectorAll('h2')].map((heading) => text(heading));
    expect(headings).toEqual([
      'About',
      'The work',
      'Selling',
      'Teaching',
      'Images',
      'The rest',
      'Review',
    ]);
    expect(text(root)).toContain('A few at a time');
    expect(text(root)).toContain('Has taught before Yes');

    // The bio is a visitor's words: shown as written, never parsed.
    expect(text(root)).toContain('<img src=x onerror="alert(1)">Potter');
    expect(root.querySelector('.answers img[onerror]')).toBeNull();

    const website = root.querySelector<HTMLAnchorElement>('a[href="https://rosaortiz.example/"]')!;
    expect(website.target).toBe('_blank');
    expect(website.rel).toBe('noopener noreferrer');
    // A portfolio that is not an http(s) address is text, not a link.
    expect(root.querySelector('a[href^="javascript"]')).toBeNull();
    expect(text(root)).toContain('javascript:alert(1)');

    expect(root.querySelector('a[href="mailto:rosa@example.com"]')).not.toBeNull();
    // The statement keeps its line break.
    const statement = [...root.querySelectorAll('dd')].find((dd) =>
      dd.textContent?.includes('Wheel-thrown'),
    );
    expect(statement?.textContent).toContain('stoneware.\nGlazes');
    expect(statement?.classList.contains('multiline')).toBe(true);
  });

  it('shows the pictures as thumbnails, named by file or by number', async () => {
    const { root } = await openSubmission(SUBMISSION);

    const images = [...root.querySelectorAll<HTMLImageElement>('.images img')];
    expect(images.map((image) => image.alt)).toEqual(['bowl.jpg', 'Submitted picture 2']);
    expect(images[0].getAttribute('src')).toBe(SUBMISSION.images[0].thumbnailUrl);
    // No thumbnail yet: the original stands in.
    expect(images[1].getAttribute('src')).toBe(SUBMISSION.images[1].originalUrl);
  });

  it('saves a status and notes under the ETag, and counts them as unsaved until then', async () => {
    const { http, fixture, root } = await openSubmission(SUBMISSION);
    const component = fixture.componentInstance;

    const select = root.querySelector<HTMLSelectElement>('select[name="status"]')!;
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    const notes = root.querySelector<HTMLTextAreaElement>('textarea[name="notes"]')!;
    notes.value = '  Strong glazes.  ';
    notes.dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(component.hasUnsavedChanges()).toBe(true);

    button(root, 'Save')!.click();
    const patch = http.expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(SUBMISSION.etag);
    expect(patch.request.body).toEqual({ status: 'Reviewed', notes: 'Strong glazes.' });
    patch.flush({ ...SUBMISSION, status: 'Reviewed', notes: 'Strong glazes.', etag: '"2"' });
    await settle(fixture);

    expect(component.hasUnsavedChanges()).toBe(false);
    expect(text(root.querySelector('header'))).toContain('Reviewed');
  });

  it('on a 409 conflict shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openSubmission(SUBMISSION);
    const theirs: Submission = {
      ...SUBMISSION,
      status: 'Declined',
      notes: 'Not a fit.',
      etag: '"theirs"',
    };

    const notes = root.querySelector<HTMLTextAreaElement>('textarea[name="notes"]')!;
    notes.value = 'Ask about a show.';
    notes.dispatchEvent(new Event('input'));
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`).flush(theirs);
    await settle(fixture);

    const compare = text(root.querySelector('.compare'));
    expect(compare).toContain('Not a fit.');
    expect(compare).toContain('Ask about a show.');
    expect(compare).toContain('Declined');
    // The editor's words are still in the form.
    expect(root.querySelector<HTMLTextAreaElement>('textarea[name="notes"]')!.value).toBe(
      'Ask about a show.',
    );

    button(root, 'Keep my changes')!.click();
    await settle(fixture);
    const retry = http.expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    // Only the notes differ from what is saved now; the status stays theirs.
    expect(retry.request.body).toEqual({ notes: 'Ask about a show.' });
  });

  it('shows a too-long note the API refused beside the field', async () => {
    const { http, fixture, root } = await openSubmission(SUBMISSION);

    const notes = root.querySelector<HTMLTextAreaElement>('textarea[name="notes"]')!;
    notes.value = 'x';
    notes.dispatchEvent(new Event('input'));
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`).flush(
      {
        code: 'validation.failed',
        detail: 'Some fields need attention.',
        errors: { notes: ['Keep notes under 4000 characters.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(root.querySelector('textarea[name="notes"]')!.getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('Keep notes under 4000 characters.');
  });

  it('says there is no such submission on a 404', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(SubmissionDetail);
    fixture.componentRef.setInput('id', SUBMISSION.id);
    fixture.detectChanges();
    http
      .expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`)
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('There’s no such submission');
  });
});

describe('MessageList', () => {
  it('lists messages and filters to the unhandled ones', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MessageList);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === MESSAGES)
      .flush({
        items: [CONTACT_MESSAGE, { ...CONTACT_MESSAGE, id: 'b', handled: true }],
        nextCursor: null,
      });
    await settle(fixture);
    const root = fixture.nativeElement as HTMLElement;

    const [first, second] = rows(root);
    expect(first).toContain('Sam Lee');
    expect(first).toContain('Are you open Sunday? I would like to visit');
    expect(first).toContain('Unhandled');
    expect(second).toContain('Handled');

    button(root, 'Unhandled')!.click();
    await settle(fixture);
    const filtered = http.expectOne((r) => r.url === MESSAGES);
    expect(filtered.request.params.get('handled')).toBe('false');
    filtered.flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(text(root)).toContain('Every message has been handled.');
  });

  it('offers a retry when the messages cannot be loaded', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(MessageList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === MESSAGES).flush(null, { status: 500, statusText: 'Error' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The messages couldn’t be loaded');
    expect(button(fixture.nativeElement, 'Try again')).toBeDefined();
  });
});

describe('MessageDetail', () => {
  it('shows the message as text with its line breaks, and the address as a mail link', async () => {
    const { root } = await openMessage({
      ...CONTACT_MESSAGE,
      message: 'Hello<script>alert(1)</script>\nSecond line',
    });

    const body = root.querySelector('.body__text')!;
    expect(body.textContent).toBe('Hello<script>alert(1)</script>\nSecond line');
    expect(body.querySelector('script')).toBeNull();
    expect(root.querySelector('a[href="mailto:sam@example.com"]')).not.toBeNull();
  });

  it('marks a message handled under the ETag', async () => {
    const { http, fixture, root } = await openMessage(CONTACT_MESSAGE);

    button(root, 'Mark handled')!.click();
    const patch = http.expectOne(`${MESSAGES}/${CONTACT_MESSAGE.id}`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.headers.get('If-Match')).toBe(CONTACT_MESSAGE.etag);
    expect(patch.request.body).toEqual({ handled: true });
    patch.flush({ ...CONTACT_MESSAGE, handled: true, etag: '"2"' });
    await settle(fixture);

    expect(button(root, 'Mark not handled')).toBeDefined();
    expect(text(root.querySelector('header'))).toContain('Handled');
  });

  it('on a 409 shows where the message now stands instead of overwriting it', async () => {
    const { http, fixture, root } = await openMessage(CONTACT_MESSAGE);

    button(root, 'Mark handled')!.click();
    http
      .expectOne(`${MESSAGES}/${CONTACT_MESSAGE.id}`)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http
      .expectOne(`${MESSAGES}/${CONTACT_MESSAGE.id}`)
      .flush({ ...CONTACT_MESSAGE, handled: true, etag: '"theirs"' });
    await settle(fixture);

    expect(text(root)).toContain('Someone else changed this message a moment ago');
    expect(button(root, 'Mark not handled')).toBeDefined();
  });
});

describe('NewsletterList', () => {
  async function openList(roles: string[]) {
    const context = await configure(roles);
    const fixture = TestBed.createComponent(NewsletterList);
    fixture.detectChanges();
    context.http
      .expectOne((r) => r.url === NEWSLETTER)
      .flush({
        items: [
          SUBSCRIBER,
          { ...SUBSCRIBER, id: 'b', email: 'artist@example.com', source: 'Submission' },
        ],
        nextCursor: 'page-2',
      });
    await settle(fixture);
    return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('lists each address with its consent and source, and hides the Admin actions from an Editor', async () => {
    const { root } = await openList(['Editor']);

    const [first, second] = rows(root);
    expect(first).toContain('reader@example.com');
    expect(first).toContain('2026-10-21');
    expect(first).toContain('Sign-up');
    expect(second).toContain('Submission');
    expect(button(root, 'Export CSV')).toBeUndefined();
    expect(button(root, 'Remove')).toBeUndefined();
    expect(text(root)).toContain('Only an Admin can export the list');
  });

  it('searches after typing pauses, and loads more under the cursor', async () => {
    const { http, fixture, root } = await openList(['Editor']);

    button(root, 'Load more')!.click();
    const more = http.expectOne((r) => r.url === NEWSLETTER);
    expect(more.request.params.get('cursor')).toBe('page-2');
    more.flush({
      items: [{ ...SUBSCRIBER, id: 'c', email: 'third@example.com' }],
      nextCursor: null,
    });
    await settle(fixture);
    expect(rows(root)).toHaveLength(3);

    const search = root.querySelector<HTMLInputElement>('input[name="search"]')!;
    search.value = 'Reader ';
    search.dispatchEvent(new Event('input'));
    http.expectNone((r) => r.url === NEWSLETTER);
    await new Promise((resolve) => setTimeout(resolve, 350));
    await settle(fixture);

    const searched = http.expectOne((r) => r.url === NEWSLETTER);
    expect(searched.request.params.get('search')).toBe('Reader');
    expect(searched.request.params.has('cursor')).toBe(false);
    searched.flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(root)).toContain('No address contains “Reader”.');
  });

  it('lets an Admin export the list as a download', async () => {
    const { http, fixture, root } = await openList(['Admin']);
    const save = vi
      .spyOn(TestBed.inject(BrowserDownload), 'save')
      .mockImplementation(() => undefined);

    button(root, 'Export CSV')!.click();
    const request = http.expectOne(`${NEWSLETTER}/export`);
    expect(request.request.method).toBe('GET');
    request.flush(new Blob(['email\r\n'], { type: 'text/csv' }), {
      headers: { 'Content-Disposition': 'attachment; filename=6311arts-newsletter-2026-10-21.csv' },
    });
    await settle(fixture);

    expect(save).toHaveBeenCalledWith(expect.any(Blob), '6311arts-newsletter-2026-10-21.csv');
  });

  it('asks before an Admin removes an address, then removes it', async () => {
    const { http, fixture, root, confirm } = await openList(['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    root.querySelector<HTMLButtonElement>('[aria-label="Remove reader@example.com"]')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Remove this address?' }));
    const request = http.expectOne(`${NEWSLETTER}/${SUBSCRIBER.id}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(rows(root)).toHaveLength(1);
    expect(rows(root)[0]).toContain('artist@example.com');
  });

  it('keeps the address when the Admin says no, and shows a refused removal in place', async () => {
    const { http, fixture, root, confirm } = await openList(['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValueOnce(false);

    root.querySelector<HTMLButtonElement>('[aria-label="Remove reader@example.com"]')!.click();
    await settle(fixture);
    http.expectNone(`${NEWSLETTER}/${SUBSCRIBER.id}`);

    ask.mockResolvedValueOnce(true);
    root.querySelector<HTMLButtonElement>('[aria-label="Remove reader@example.com"]')!.click();
    await settle(fixture);
    http
      .expectOne(`${NEWSLETTER}/${SUBSCRIBER.id}`)
      .flush({ code: 'access.forbidden' }, { status: 403, statusText: 'Forbidden' });
    await settle(fixture);

    expect(rows(root)).toHaveLength(2);
    expect(text(root)).toContain('Only an Admin can do that.');
  });

  it('offers a retry when the list cannot be loaded, and an empty state when it is empty', async () => {
    const { http } = await configure(['Editor']);
    const fixture = TestBed.createComponent(NewsletterList);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === NEWSLETTER)
      .flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('The newsletter list couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === NEWSLETTER).flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('No one on the list yet');
  });
});

describe('Forms routes', () => {
  async function at(url: string) {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes, withComponentInputBinding())],
    });
    const http = TestBed.inject(HttpTestingController);
    const loaded = TestBed.inject(SessionService).ensureLoaded();
    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Editor'] });
    await loaded;
    const harness = await RouterTestingHarness.create(url);
    return { http, harness, router: TestBed.inject(Router) };
  }

  it('opens the section on its first tab, with the tabs marking where it is', async () => {
    const { http, harness, router } = await at('/forms');

    expect(router.url).toBe('/forms/submissions');
    http.expectOne((r) => r.url === SUBMISSIONS).flush({ items: [], nextCursor: null });
    await harness.fixture.whenStable();
    harness.detectChanges();

    const current = (harness.fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Forms"] [aria-current="page"]',
    );
    expect(text(current)).toBe('Submissions');
  });

  it('opens a message and a submission at their own addresses for an Editor', async () => {
    const { http, harness, router } = await at(`/forms/messages/${CONTACT_MESSAGE.id}`);

    expect(router.url).toBe(`/forms/messages/${CONTACT_MESSAGE.id}`);
    http.expectOne(`${MESSAGES}/${CONTACT_MESSAGE.id}`).flush(CONTACT_MESSAGE);

    await harness.navigateByUrl(`/forms/submissions/${SUBMISSION.id}`);
    expect(router.url).toBe(`/forms/submissions/${SUBMISSION.id}`);
    http.expectOne(`${SUBMISSIONS}/${SUBMISSION.id}`).flush(SUBMISSION);
  });
});
