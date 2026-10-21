import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { ClassOffering } from '../../models/class';
import { CLASS_OFFERING, SYNCED_CLASS, toClassSummary } from '../../models/class.fixtures';
import { ClassEditor } from './class-editor/class-editor';
import { ClassList } from './class-list/class-list';
import { newRunDraft, runDraftsOf, runErrorOf, runInputsOf, runsEqual } from './run-list/run-list';
import { sessionDraftsOf, sessionInputsOf } from './session-grid/session-grid';

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
  it('lists classes with when they next meet, their status, their instructors and how many runs they have', async () => {
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
    expect(text(rows[0])).toContain('Gwen Schilling');
    expect(text(rows[0].querySelectorAll('td')[2])).toBe('1'); // one run
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
    http
      .expectOne(ONE)
      .flush(
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

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such class');
    expect(root.querySelector('a[href="/classes"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a class with one run, its sessions as instants and a suggested SKU', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'Intro to Screen Printing');
    type(root, 'summary', 'Burn your first screen.');
    type(root, 'runLabel0', 'Fall 2026');
    type(root, 'runCadence0', 'Saturdays');
    type(root, 'runCapacity0', '10');
    type(root, 'runPrice0', '300');
    type(root, 'runSku0', 'cls-4-1');
    type(root, 'run0SessionDate0', '2026-10-24');
    type(root, 'run0SessionStarts0', '14:00');
    type(root, 'run0SessionEnds0', '17:00');
    await settle(fixture);

    expect(input(root, 'slug').value).toBe('intro-to-screen-printing');
    button(root, 'Create class')!.click();
    const post = http.expectOne(BASE);

    expect(post.request.body).toEqual({
      title: 'Intro to Screen Printing',
      slug: 'intro-to-screen-printing',
      summary: 'Burn your first screen.',
      runs: [
        {
          capacity: 10,
          label: 'Fall 2026',
          cadence: 'Saturdays',
          sku: 'CLS-4-1',
          displayTimeZone: 'America/Chicago',
          startingPriceCents: 30000,
          // 2 p.m. in Chicago in October is 19:00 UTC.
          sessions: [
            { startsAt: '2026-10-24T19:00:00+00:00', endsAt: '2026-10-24T22:00:00+00:00' },
          ],
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

  it('chooses a run’s instructor from the instructors list', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);

    button(root, 'Remove Gwen Schilling')!.click();
    await settle(fixture);
    type(root, 'instructorSearch', 'nich');
    await new Promise((resolve) => setTimeout(resolve, 350));
    http
      .expectOne((r) => r.url === '/api/v1/cms/instructors')
      .flush({
        items: [
          {
            id: 'nick',
            name: 'Nicholas Waguespack',
            slug: 'nicholas-waguespack',
            medium: 'Printmaking',
            active: true,
            status: 'Published',
            portraitMediaId: null,
            updatedAt: '2026-10-20T12:00:00+00:00',
          },
        ],
        nextCursor: null,
      });
    await settle(fixture);
    button(root, 'Choose Nicholas Waguespack')!.click();
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);

    expect(patch.request.body.runs[0].instructorId).toBe('nick');
    expect(patch.request.body.runs[0].id).toBe(CLASS_OFFERING.runs[0].id);
  });

  it('refuses an incomplete session, a backwards one and a repeated SKU before asking the API', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'title', 'Course');
    type(root, 'runCapacity0', '10');
    type(root, 'run0SessionDate0', '2026-10-24');
    type(root, 'run0SessionStarts0', '17:00');
    type(root, 'run0SessionEnds0', '14:00');
    await settle(fixture);
    button(root, 'Create class')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(text(root)).toContain('Run 1, session 1 can’t end before it starts.');

    type(root, 'run0SessionEnds0', '20:00');
    type(root, 'runSku0', 'CLS-9-1');
    button(root, 'Add run')!.click();
    await settle(fixture);
    type(root, 'runSku1', 'cls-9-1');
    type(root, 'run1SessionDate0', '2026-11-07');
    type(root, 'run1SessionStarts0', '17:00');
    type(root, 'run1SessionEnds0', '20:00');
    await settle(fixture);
    button(root, 'Create class')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(text(root)).toContain('Two runs have the SKU CLS-9-1.');
  });

  it('sends only the runs when they change, keeping the ids of the run and sessions that stay', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);
    const run = CLASS_OFFERING.runs[0];

    expect(input(root, 'runSku0').value).toBe('CLS-4-1');
    expect(input(root, 'run0SessionStarts0').value).toBe('14:00');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    type(root, 'runCapacity0', '12');
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);

    expect(patch.request.headers.get('If-Match')).toBe(CLASS_OFFERING.etag);
    expect(patch.request.body).toEqual({
      runs: [
        {
          capacity: 12,
          id: run.id,
          label: 'Saturday afternoons',
          instructorId: run.instructor!.id,
          cadence: 'Saturdays',
          sku: 'CLS-4-1',
          displayTimeZone: 'America/Chicago',
          startingPriceCents: 30000,
          sessions: [
            {
              id: run.sessions[0].id,
              startsAt: '2026-10-24T19:00:00+00:00',
              endsAt: '2026-10-24T22:00:00+00:00',
            },
            {
              id: run.sessions[1].id,
              startsAt: '2026-10-31T19:00:00+00:00',
              endsAt: '2026-10-31T22:00:00+00:00',
            },
          ],
        },
      ],
    });
  });

  it('shows a SKU clash from the API on the runs and keeps the edit', async () => {
    const { http, fixture, root } = await openEditor(CLASS_OFFERING);

    type(root, 'runSku0', 'ART-7');
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(ONE).flush(
      {
        code: 'cms.sku_taken',
        detail: 'The SKU “ART-7” is already in use.',
        errors: { runs: ['“ART-7” belongs to another class run or shop item. Choose another.'] },
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);

    expect(text(root.querySelector('cms-class-runs .error'))).toBe(
      '“ART-7” belongs to another class run or shop item. Choose another.',
    );
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('shows what Square holds read-only once it has reported a run’s price, and never sends it', async () => {
    const { http, fixture, root } = await openEditor(SYNCED_CLASS);

    // No editable price field: the change is a command to Square (ADR 0032, arriving in 5.4a).
    expect(input(root, 'runPrice0')).toBeNull();
    const runs = root.querySelector('cms-class-runs')!;
    expect(text(runs)).toContain('$325.00 a seat in Square.');
    expect(text(runs)).toContain('6 of 10 seats left.');
    expect(button(root, 'Change price for Run 1 (Saturday afternoons)')?.disabled).toBe(true);
    expect(button(root, 'Remove Run 1 (Saturday afternoons)')?.disabled).toBe(true);
    expect(text(runs)).toContain('can’t be removed here');

    type(root, 'runCapacity0', '14');
    await settle(fixture);
    button(root, 'Save')!.click();
    const body = http.expectOne(ONE).request.body;
    expect(body.runs[0].capacity).toBe(14);
    expect('startingPriceCents' in body.runs[0]).toBe(false);
  });

  it('adds a run that carries the last one’s details over, and removes one not in Square yet', async () => {
    const { fixture, root } = await openEditor(CLASS_OFFERING);

    button(root, 'Add run')!.click();
    await settle(fixture);

    // The next term is usually the same class again: instructor, seats and price carry over, with a free SKU and
    // a first session a week after the last run's.
    expect(input(root, 'runSku1').value).toBe('CLS-4-2');
    expect(input(root, 'runCapacity1').value).toBe('10');
    expect(input(root, 'runPrice1').value).toBe('300');
    expect(input(root, 'run1SessionDate0').value).toBe('2026-11-07');
    expect(text(root.querySelectorAll('cms-instructor-picker')[1])).toContain('Gwen Schilling');

    button(root, 'Remove Run 2')!.click();
    await settle(fixture);
    expect(root.querySelectorAll('[data-run]')).toHaveLength(1);
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('keeps focus in a run’s sessions when one is removed, and marks them when the runs are refused', async () => {
    const { fixture, root } = await openEditor(CLASS_OFFERING);
    const removeFirst = 'Remove Run 1 (Saturday afternoons) session 1';

    button(root, removeFirst)!.click();
    await settle(fixture);

    // The clicked button went with its row, so focus moved to the row that took its place.
    expect(document.activeElement?.getAttribute('aria-label')).toBe(removeFirst);

    button(root, removeFirst)!.click();
    await settle(fixture);
    expect(text(document.activeElement)).toBe('Add session');

    button(root, 'Add session')!.click();
    await settle(fixture);
    type(root, 'run0SessionDate0', '');
    await settle(fixture);
    button(root, 'Save')!.click();
    await settle(fixture);

    expect(input(root, 'run0SessionDate0').getAttribute('aria-invalid')).toBe('true');
    expect(input(root, 'run0SessionDate0').getAttribute('aria-describedby')).toBe(
      root.querySelector('cms-class-runs .error')?.id,
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

describe('run drafts', () => {
  it('round-trips the saved runs through their own clock', () => {
    const runs = runDraftsOf(CLASS_OFFERING.runs);

    expect(runs[0].sessions.map((row) => [row.date, row.starts, row.ends])).toEqual([
      ['2026-10-24', '14:00', '17:00'],
      ['2026-10-31', '14:00', '17:00'],
    ]);
    expect(runInputsOf(runs)?.[0].sessions).toEqual([
      {
        id: CLASS_OFFERING.runs[0].sessions[0].id,
        startsAt: '2026-10-24T19:00:00+00:00',
        endsAt: '2026-10-24T22:00:00+00:00',
      },
      {
        id: CLASS_OFFERING.runs[0].sessions[1].id,
        startsAt: '2026-10-31T19:00:00+00:00',
        endsAt: '2026-10-31T22:00:00+00:00',
      },
    ]);
    expect(runsEqual(runs, runDraftsOf(CLASS_OFFERING.runs))).toBe(true);
  });

  it('is nothing to send while a run is half filled in, and says which run', () => {
    const runs = [newRunDraft([], 4)];

    expect(runInputsOf(runs)).toBeNull();
    expect(runErrorOf(runs)).toBe('Run 1 needs a number of seats, from 1 to 500.');
    expect(runErrorOf([{ ...runs[0], capacity: '8' }])).toBe(
      'Run 1, session 1 needs a date, a start time and an end time.',
    );
  });

  it('keeps the same wall-clock time across the end of daylight saving', () => {
    const october = sessionDraftsOf(CLASS_OFFERING.runs[0].sessions, 'America/Chicago')[0];
    const november = { ...october, key: 'n', id: null, date: '2026-11-07' };

    const inputs = sessionInputsOf([november], 'America/Chicago')!;

    // 2 p.m. Chicago is 19:00 UTC in October and 20:00 UTC in November.
    expect(inputs[0].startsAt).toBe('2026-11-07T20:00:00+00:00');
  });
});
