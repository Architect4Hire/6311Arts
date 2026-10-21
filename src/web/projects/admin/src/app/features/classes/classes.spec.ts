import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { ClassOffering } from '../../models/class';
import { CLASS_OFFERING, SYNCED_CLASS, toClassSummary } from '../../models/class.fixtures';
import { ClassEditor } from './class-editor';
import { ClassList } from './class-list';
import {
  newSessionDraft,
  sessionDraftsOf,
  sessionInputsOf,
  sessionsEqual,
} from './session-grid';

const BASE = '/api/v1/cms/classes';
const ONE = `${BASE}/${CLASS_OFFERING.id}`;

async function configure(roles: string[] = ['Editor']) {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
  return { http, confirm: TestBed.inject(ConfirmService), router: TestBed.inject(Router) };
}

/** Lets awaited HTTP promises settle, then renders. */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find(
    (candidate) => text(candidate) === label || candidate.getAttribute('aria-label') === label,
  );
}

function input(root: HTMLElement, name: string): HTMLInputElement | HTMLTextAreaElement {
  return root.querySelector(`[name="${name}"]`) as HTMLInputElement | HTMLTextAreaElement;
}

function type(root: HTMLElement, name: string, value: string) {
  const control = input(root, name);
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

async function openEditor(offering: ClassOffering | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(ClassEditor);
  if (offering) fixture.componentRef.setInput('id', offering.id);
  fixture.detectChanges();
  if (offering) {
    context.http.expectOne(`${BASE}/${offering.id}`).flush(offering);
    await settle(fixture);
  }
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ClassList', () => {
  it('lists classes with when they next meet, their status and how many sessions they have', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ClassList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe('Loading classes…');
    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          toClassSummary(CLASS_OFFERING),
          {
            ...toClassSummary(CLASS_OFFERING),
            id: 'b',
            title: 'Intro to Scratchboard',
            status: 'Published',
            nextSessionStartsAt: null,
          },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual([
      'Intro to Screen Printing',
      'Intro to Scratchboard',
    ]);
    expect(text(rows[0])).toContain('Printmaking · Beginner');
    expect(text(rows[0])).toContain('Draft');
    expect(text(rows[0])).toContain('2'); // two sessions
    expect(text(rows[1])).toContain('No sessions to come');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(`/classes/${CLASS_OFFERING.id}`);
  });

  it('asks again with the status chosen, and says when nothing matches', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ClassList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    button(fixture.nativeElement, 'Published')!.click();
    await settle(fixture);
    const second = http.expectOne((r) => r.url === BASE);

    expect(second.request.params.get('status')).toBe('Published');
    second.flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('No class is published.');
  });

  it('offers a retry when the classes cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. All four existed in the
    // component and only the happy path was tested, so a 503 leaving "Loading classes…" on screen for good
    // would have passed (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(ClassList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading classes…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The classes couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('ClassEditor', () => {
  it('offers a retry when the record cannot be loaded, and asks again when it is used', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ClassEditor);
    fixture.componentRef.setInput('id', CLASS_OFFERING.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This class couldn’t be loaded');

    // The retry asks again rather than showing a form built from nothing.
    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne(ONE).flush(
      { code: 'resource.not_found', title: 'Not found.' },
      { status: 404, statusText: 'Not Found' },
    );
    await settle(fixture);

    expect(text(fixture.nativeElement)).not.toContain('This class couldn’t be loaded');
  });

  it('says there is no such record on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ClassEditor);
    fixture.componentRef.setInput('id', CLASS_OFFERING.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(
      { code: 'resource.not_found', title: 'Not found.' },
      { status: 404, statusText: 'Not Found' },
    );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such class');
    expect(root.querySelector('a[href="/classes"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a class with its sessions as instants and a suggested SKU', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'Intro to Screen Printing');
    type(root, 'summary', 'Burn your first screen.');
    type(root, 'price', '300');
    type(root, 'sessionDate0', '2026-10-24');
    type(root, 'sessionStarts0', '14:00');
    type(root, 'sessionEnds0', '17:00');
    type(root, 'sessionCapacity0', '10');
    type(root, 'sessionSku0', 'cls-4-1');
    await settle(fixture);

    expect(input(root, 'slug').value).toBe('intro-to-screen-printing');
    button(root, 'Create class')!.click();
    const post = http.expectOne(BASE);

    expect(post.request.body).toEqual({
      title: 'Intro to Screen Printing',
      slug: 'intro-to-screen-printing',
      summary: 'Burn your first screen.',
      startingPriceCents: 30000,
      sessions: [
        {
          // 2 p.m. in Chicago in October is 19:00 UTC.
          startsAt: '2026-10-24T19:00:00+00:00',
          endsAt: '2026-10-24T22:00:00+00:00',
          capacity: 10,
          sku: 'CLS-4-1',
          displayTimeZone: 'America/Chicago',
        },
      ],
    });

    post.flush(CLASS_OFFERING);
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/classes', CLASS_OFFERING.id], {
      replaceUrl: true,
      state: { classCreated: true },
    });
  });

  it('refuses an incomplete session, a backwards one and a repeated SKU before asking the API', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'title', 'Course');
    type(root, 'sessionDate0', '2026-10-24');
    type(root, 'sessionStarts0', '17:00');
    type(root, 'sessionEnds0', '14:00');
    type(root, 'sessionCapacity0', '10');
    await settle(fixture);
    button(root, 'Create class')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(text(root)).toContain('Session 1 can’t end before it starts.');

    type(root, 'sessionEnds0', '20:00');
    type(root, 'sessionSku0', 'CLS-9-1');
    button(root, 'Add session')!.click();
    await settle(fixture);
    type(root, 'sessionSku1', 'cls-9-1');
    await settle(fixture);
    button(root, 'Create class')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(text(root)).toContain('Two sessions have the SKU CLS-9-1.');
  });

  it('sends only the sessions when they change, keeping the ids of the ones that stay', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);

    expect(input(root, 'sessionSku0').value).toBe('CLS-4-1');
    expect(input(root, 'sessionStarts0').value).toBe('14:00');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    type(root, 'sessionCapacity0', '12');
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);

    expect(patch.request.headers.get('If-Match')).toBe(CLASS_OFFERING.etag);
    expect(patch.request.body).toEqual({
      sessions: [
        {
          id: CLASS_OFFERING.sessions[0].id,
          startsAt: '2026-10-24T19:00:00+00:00',
          endsAt: '2026-10-24T22:00:00+00:00',
          capacity: 12,
          sku: 'CLS-4-1',
          displayTimeZone: 'America/Chicago',
        },
        {
          id: CLASS_OFFERING.sessions[1].id,
          startsAt: '2026-10-31T19:00:00+00:00',
          endsAt: '2026-10-31T22:00:00+00:00',
          capacity: 10,
          sku: 'CLS-4-2',
          displayTimeZone: 'America/Chicago',
        },
      ],
    });
  });

  it('shows a SKU clash from the API on the sessions and keeps the edit', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);

    type(root, 'sessionSku0', 'ART-7');
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(ONE).flush(
      {
        code: 'cms.sku_taken',
        detail: 'The SKU “ART-7” is already in use.',
        errors: {
          sessions: ['“ART-7” belongs to another class session or shop item. Choose another.'],
        },
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);

    expect(text(root.querySelector('cms-session-grid .error'))).toBe(
      '“ART-7” belongs to another class session or shop item. Choose another.',
    );
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('shows what Square holds read-only once it has reported a price', async () => {
    const { root } = await openEditor(SYNCED_CLASS);

    // No editable price field: the change is a command to Square (ADR 0032, arriving in 5.4a).
    expect(input(root, 'price')).toBeNull();
    expect(text(root.querySelector('.price'))).toBe('$300.00');
    expect(button(root, 'Change price')?.disabled).toBe(true);

    const grid = root.querySelector('cms-session-grid')!;
    expect(text(grid)).toContain('$325.00');
    expect(text(grid)).toContain('6 of 10 seats left');
    expect(button(root, 'Remove session 1')?.disabled).toBe(true);
    expect(text(grid)).toContain('can’t be removed here');
  });

  it('adds and removes a session that is not in Square yet', async () => {
    const { fixture, root } = await openEditor(CLASS_OFFERING);

    button(root, 'Remove session 2')!.click();
    await settle(fixture);
    expect(root.querySelectorAll('tbody tr')).toHaveLength(1);

    button(root, 'Add session')!.click();
    await settle(fixture);

    // A week after the one before it, at the same time, with a free suggested SKU.
    expect(input(root, 'sessionDate1').value).toBe('2026-10-31');
    expect(input(root, 'sessionSku1').value).toBe('CLS-4-2');
  });

  it('keeps focus in the grid when a session is removed, and marks the rows when they are refused', async () => {
    const { fixture, root } = await openEditor(CLASS_OFFERING);

    button(root, 'Remove session 1')!.click();
    await settle(fixture);

    // The clicked button went with its row, so focus moved to the row that took its place.
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove session 1');

    button(root, 'Remove session 1')!.click();
    await settle(fixture);
    expect(text(document.activeElement)).toBe('Add session');

    button(root, 'Add session')!.click();
    await settle(fixture);
    type(root, 'sessionDate0', '');
    await settle(fixture);
    button(root, 'Save')!.click();
    await settle(fixture);

    expect(input(root, 'sessionDate0').getAttribute('aria-invalid')).toBe('true');
    expect(input(root, 'sessionDate0').getAttribute('aria-describedby')).toBe(
      root.querySelector('cms-session-grid .error')?.id,
    );
  });

  it('says a published class’s old address will redirect when its slug changes', async () => {
    const published: ClassOffering = {
      ...CLASS_OFFERING,
      status: 'Published',
      publishedAt: '2026-10-20T12:00:00+00:00',
      slugLocked: true,
    };
    const { fixture, root } = await openEditor(published);

    expect(text(root)).toContain('published, so changing it sends visitors');
    type(root, 'slug', 'screen-printing');
    await settle(fixture);

    expect(text(root)).toContain('visitors to /classes/intro-to-screen-printing are sent here');
  });

  it('on a 409 shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);
    const theirs: ClassOffering = {
      ...CLASS_OFFERING,
      title: 'Screen Printing I',
      etag: '"theirs"',
    };

    type(root, 'summary', 'Five Saturdays in the print shop.');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = root.querySelector('.compare')!;
    expect(text(comparison)).toContain('Screen Printing I');
    expect(text(comparison)).toContain('Five Saturdays in the print shop.');
    expect(input(root, 'summary').value).toBe('Five Saturdays in the print shop.');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
  });

  it('publishes and schedules the saved version, and archive is for Admins', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING, ['Editor']);

    expect(button(root, 'Archive')).toBeUndefined();
    button(root, 'Publish')!.click();
    const publish = http.expectOne(`${ONE}/publish`);
    expect(publish.request.headers.get('If-Match')).toBe(CLASS_OFFERING.etag);
    publish.flush({ ...CLASS_OFFERING, status: 'Published', slugLocked: true });
    await settle(fixture);

    expect(text(root.querySelector('ui-status-chip'))).toContain('Published');
  });

  it('refuses to publish while there are unsaved changes', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);

    type(root, 'summary', 'Changed.');
    await settle(fixture);

    expect(button(root, 'Publish')?.disabled).toBe(true);
    expect(text(root)).toContain('Save your changes first');
    http.expectNone(`${ONE}/publish`);
  });

  it('lets an Admin archive after confirming', async () => {
    const { http, fixture, root, confirm } = await openEditor(CLASS_OFFERING, ['Admin']);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    button(root, 'Archive')!.click();
    await settle(fixture);
    const archive = http.expectOne(`${ONE}/archive`);
    archive.flush({ ...CLASS_OFFERING, status: 'Archived' });
    await settle(fixture);

    expect(text(root.querySelector('ui-status-chip'))).toContain('Archived');
  });
});

describe('session drafts', () => {
  it('round-trips the saved sessions through the gallery clock', () => {
    const rows = sessionDraftsOf(CLASS_OFFERING.sessions);

    expect(rows.map((row) => [row.date, row.starts, row.ends])).toEqual([
      ['2026-10-24', '14:00', '17:00'],
      ['2026-10-31', '14:00', '17:00'],
    ]);
    expect(sessionInputsOf(rows)).toEqual([
      {
        id: CLASS_OFFERING.sessions[0].id,
        startsAt: '2026-10-24T19:00:00+00:00',
        endsAt: '2026-10-24T22:00:00+00:00',
        capacity: 10,
        sku: 'CLS-4-1',
        displayTimeZone: 'America/Chicago',
      },
      {
        id: CLASS_OFFERING.sessions[1].id,
        startsAt: '2026-10-31T19:00:00+00:00',
        endsAt: '2026-10-31T22:00:00+00:00',
        capacity: 10,
        sku: 'CLS-4-2',
        displayTimeZone: 'America/Chicago',
      },
    ]);
    expect(sessionsEqual(rows, sessionDraftsOf(CLASS_OFFERING.sessions))).toBe(true);
  });

  it('is nothing to send while a row is half filled in', () => {
    const rows = [newSessionDraft([], 4)];

    expect(sessionInputsOf(rows)).toBeNull();
  });

  it('keeps the same wall-clock time across the end of daylight saving', () => {
    const october = sessionDraftsOf(CLASS_OFFERING.sessions)[0];
    const november = { ...october, key: 'n', id: null, date: '2026-11-07' };

    const inputs = sessionInputsOf([november])!;

    // 2 p.m. Chicago is 19:00 UTC in October and 20:00 UTC in November.
    expect(inputs[0].startsAt).toBe('2026-11-07T20:00:00+00:00');
  });
});
