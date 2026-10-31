import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { ARTIST, toArtistSummary } from '../../models/artist.fixtures';
import { Exhibition } from '../../models/exhibition';
import { EXHIBITION, toExhibitionSummary } from '../../models/exhibition.fixtures';
import { ExhibitionEditor } from './exhibition-editor/exhibition-editor';
import { ExhibitionList } from './exhibition-list/exhibition-list';

const BASE = '/api/v1/cms/exhibitions';
const ONE = `${BASE}/${EXHIBITION.id}`;
const ARTISTS = '/api/v1/cms/artists';

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

async function openEditor(exhibition: Exhibition | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(ExhibitionEditor);
  if (exhibition) fixture.componentRef.setInput('id', exhibition.id);
  fixture.detectChanges();
  if (exhibition) {
    context.http.expectOne(`${BASE}/${exhibition.id}`).flush(exhibition);
    await settle(fixture);
  }
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ExhibitionList', () => {
  it('lists shows newest first with their run, status and number of artists', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ExhibitionList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe(
      'Loading exhibitions…',
    );
    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          toExhibitionSummary(EXHIBITION),
          { ...toExhibitionSummary(EXHIBITION), id: 'b', title: 'Queue', status: 'Published' },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual(['Boats & Pianos', 'Queue']);
    expect(text(rows[0])).toContain('Oct 24 – Dec 5, 2026');
    expect(text(rows[0])).toContain('Draft');
    expect(text(rows[1])).toContain('Published');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(`/exhibitions/${EXHIBITION.id}`);
  });

  it('asks again with the status chosen, and says when nothing matches', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ExhibitionList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture.nativeElement)).toContain('No exhibitions yet');

    button(fixture.nativeElement, 'Archived')!.click();
    fixture.detectChanges();
    const filtered = http.expectOne((r) => r.url === BASE);
    expect(filtered.request.params.get('status')).toBe('Archived');
    filtered.flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('No exhibition is archived.');
  });

  it('offers a retry when the exhibitions cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. All four existed in the
    // component and only the happy path was tested, so a 503 leaving "Loading exhibitions…" on screen for good
    // would have passed (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(ExhibitionList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading exhibitions…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The exhibitions couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('ExhibitionEditor', () => {
  it('offers a retry when the record cannot be loaded, and asks again when it is used', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ExhibitionEditor);
    fixture.componentRef.setInput('id', EXHIBITION.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This exhibition couldn’t be loaded');

    // The retry asks again rather than showing a form built from nothing.
    button(fixture.nativeElement, 'Try again')!.click();
    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    expect(text(fixture.nativeElement)).not.toContain('This exhibition couldn’t be loaded');
  });

  it('says there is no such record on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ExhibitionEditor);
    fixture.componentRef.setInput('id', EXHIBITION.id);
    fixture.detectChanges();

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such exhibition');
    expect(root.querySelector('a[href="/exhibitions"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a show with its dates, a reception typed in gallery time, and the address the API makes from its title', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'Boats & Pianos');
    type(root, 'startDate', '2026-10-24');
    type(root, 'endDate', '2026-12-05');
    await settle(fixture);
    expect(input(root, 'slug').value).toBe('boats-pianos');
    expect(input(root, 'slug').disabled).toBe(true);

    (input(root, 'hasReception') as HTMLInputElement).click();
    await settle(fixture);
    expect(input(root, 'receptionDate').value).toBe('2026-10-24');
    type(root, 'receptionStarts', '18:00');
    type(root, 'receptionEnds', '21:00');
    type(root, 'receptionNote', ' Free and open to all ');
    await settle(fixture);

    button(root, 'Create exhibition')!.click();
    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({
      title: 'Boats & Pianos',
      startDate: '2026-10-24',
      endDate: '2026-12-05',
      // 6 pm in Chicago in late October is 11 pm UTC (daylight time).
      reception: {
        startsAt: '2026-10-24T23:00:00+00:00',
        endsAt: '2026-10-25T02:00:00+00:00',
        note: 'Free and open to all',
      },
    });
    create.flush({ ...EXHIBITION, artists: [] }, { status: 201, statusText: 'Created' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/exhibitions', EXHIBITION.id], {
      replaceUrl: true,
      state: { exhibitionCreated: true },
    });
  });

  it('refuses a show that ends before it starts, and a reception that ends before it begins, before asking the API', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'title', 'Backwards');
    type(root, 'startDate', '2026-10-24');
    type(root, 'endDate', '2026-10-01');
    (input(root, 'hasReception') as HTMLInputElement).click();
    await settle(fixture);
    type(root, 'receptionStarts', '21:00');
    type(root, 'receptionEnds', '18:00');
    await settle(fixture);
    button(root, 'Create exhibition')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(input(root, 'endDate').getAttribute('aria-invalid')).toBe('true');
    expect(input(root, 'receptionEnds').getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('The show can’t end before it starts.');
  });

  it('shows a saved reception on the gallery clock and sends only what changed', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);

    expect(input(root, 'receptionStarts').value).toBe('18:00');
    expect(input(root, 'receptionEnds').value).toBe('21:00');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    type(root, 'receptionEnds', '22:00');
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);
    expect(patch.request.headers.get('If-Match')).toBe(EXHIBITION.etag);
    expect(patch.request.body).toEqual({
      reception: {
        startsAt: '2026-10-24T23:00:00+00:00',
        endsAt: '2026-10-25T03:00:00+00:00',
        note: 'Free and open to all',
      },
    });
  });

  it('removes a reception with removeReception', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);

    (input(root, 'hasReception') as HTMLInputElement).click();
    await settle(fixture);
    expect(input(root, 'receptionDate')).toBeNull();
    button(root, 'Save')!.click();

    expect(http.expectOne(ONE).request.body).toEqual({ removeReception: true });
  });

  it('reorders, removes and adds credits, offering only artists who can be credited', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);

    button(root, 'Move Piloto up')!.click();
    await settle(fixture);
    expect([...root.querySelectorAll('.credit__name')].map((name) => text(name))).toEqual([
      'Piloto',
      'Brian Eaton',
    ]);
    // Focus stays with the artist that moved; at the top its "up" is disabled, so "down" has it.
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Move Piloto down');

    button(root, 'Remove Brian Eaton')!.click();
    await settle(fixture);

    // Fake timers only across the search's pause: whenStable waits on real ones.
    vi.useFakeTimers();
    try {
      type(root, 'artistSearch', 'ha');
      vi.advanceTimersByTime(300);
    } finally {
      vi.useRealTimers();
    }
    const search = http.expectOne((r) => r.url === ARTISTS);
    expect(search.request.params.get('search')).toBe('ha');
    search.flush({
      items: [
        toArtistSummary(ARTIST),
        { ...toArtistSummary(ARTIST), id: 'gone', name: 'Gone Artist', status: 'Archived' },
        { ...toArtistSummary(ARTIST), id: 'artist-2', name: 'Piloto', status: 'Published' },
      ],
      nextCursor: null,
    });
    await settle(fixture);

    // Archived and already-credited artists are not offered.
    expect(button(root, 'Add Gone Artist')).toBeUndefined();
    expect(button(root, 'Add Piloto')).toBeUndefined();
    button(root, 'Add Carrie Haase')!.click();
    await settle(fixture);
    expect(text(root.querySelector('cms-artist-credits ol'))).toContain('Carrie Haase Draft');

    button(root, 'Save')!.click();
    expect(http.expectOne(ONE).request.body).toEqual({ artistIds: ['artist-2', ARTIST.id] });
  });

  it('shows an archived-artist refusal on the credits', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);

    button(root, 'Move Piloto up')!.click();
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(ONE).flush(
      {
        code: 'validation.failed',
        detail: 'An archived artist can’t be credited.',
        errors: {
          artistIds: ['Piloto is archived, so can’t be credited. Remove them from the list.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root.querySelector('cms-artist-credits .error'))).toBe(
      'Piloto is archived, so can’t be credited. Remove them from the list.',
    );
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('on a 409 shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);
    const theirs: Exhibition = { ...EXHIBITION, title: 'Boats and Pianos', etag: '"theirs"' };

    type(root, 'summary', 'Reclaimed wood, boats and pianos.');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = root.querySelector('.compare')!;
    expect(text(comparison)).toContain('Boats and Pianos');
    expect(text(comparison)).toContain('Reclaimed wood, boats and pianos.');
    expect(text(comparison)).toContain('Brian Eaton, Piloto');
    expect(input(root, 'summary').value).toBe('Reclaimed wood, boats and pianos.');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    // Only what this editor changed: their new title is not sent back over with the one this editor loaded.
    expect(retry.request.body).toEqual({ summary: 'Reclaimed wood, boats and pianos.' });
  });

  it('says what publishing still needs beside each field', async () => {
    const { http, fixture, root } = await openEditor({ ...EXHIBITION, summary: null });

    button(root, 'Publish')!.click();
    http.expectOne(`${ONE}/publish`).flush(
      {
        code: 'validation.failed',
        detail: 'This exhibition isn’t ready to publish yet.',
        errors: { summary: ['Write a summary first: the site’s card and search results show it.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root)).toContain('This exhibition isn’t ready to publish yet.');
    expect(input(root, 'summary').getAttribute('aria-invalid')).toBe('true');
  });

  it('keeps the web address as it is whatever the title becomes, and offers archive to Admins only', async () => {
    const published: Exhibition = {
      ...EXHIBITION,
      status: 'Published',
      slugLocked: true,
      publishedAt: EXHIBITION.createdAt,
    };
    const editor = await openEditor(published);
    expect(input(editor.root, 'slug').disabled).toBe(true);
    expect(text(editor.root)).toContain('6311arts.com/exhibitions/boats-pianos');
    type(editor.root, 'title', 'Boats and Pianos');
    await settle(editor.fixture);
    expect(input(editor.root, 'slug').value).toBe('boats-pianos');
    expect(button(editor.root, 'Archive')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm } = await openEditor(published, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    button(root, 'Archive')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(
      expect.objectContaining({ heading: 'Archive this exhibition?' }),
    );
    http.expectOne(`${ONE}/archive`).flush({ ...published, status: 'Archived', etag: '"v2"' });
    await settle(fixture);
    expect(text(root.querySelector('header'))).toContain('Archived');
  });

  it('says plainly when the exhibition does not exist', async () => {
    const { http, fixture, root } = await openEditor(null);
    fixture.componentRef.setInput('id', 'missing');
    fixture.detectChanges();
    http
      .expectOne(`${BASE}/missing`)
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(text(root)).toContain('There’s no such exhibition');
  });

  it('schedules the exhibition and offers a preview link', async () => {
    const { http, fixture, root } = await openEditor(EXHIBITION);

    type(root, 'scheduleDate', '2026-12-24');
    type(root, 'scheduleTime', '17:00');
    await settle(fixture);
    button(root, 'Schedule')!.click();

    const schedule = http.expectOne(`${ONE}/schedule`);
    expect(schedule.request.headers.get('If-Match')).toBe(EXHIBITION.etag);
    expect(schedule.request.body).toEqual({ scheduledFor: '2026-12-24T23:00:00+00:00' });
    schedule.flush({
      ...EXHIBITION,
      status: 'Scheduled',
      scheduledFor: '2026-12-24T23:00:00+00:00',
      etag: '"v2"',
    });
    await settle(fixture);
    expect(text(root)).toContain('Publishing on');

    button(root, 'Preview')!.click();
    const minted = http.expectOne('/api/v1/cms/preview');
    expect(minted.request.body).toEqual({ entityType: 'exhibition', entityId: EXHIBITION.id });
    minted.flush({
      token: 'CfDJ8-token',
      path: '/preview/CfDJ8-token',
      expiresAt: '2026-10-20T12:30:00+00:00',
    });
    await settle(fixture);
    expect(
      (root.querySelector('a[href^="/preview/"]') as HTMLAnchorElement).getAttribute('href'),
    ).toBe('/preview/CfDJ8-token');
  });

  it('offers delete to Admins only, and returns to the list', async () => {
    // ADR 0052: Admin only, like archive, and offered whatever the status.
    const editor = await openEditor(EXHIBITION);
    expect(button(editor.root, 'Delete exhibition')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm, router } = await openEditor(EXHIBITION, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    button(root, 'Delete exhibition')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(
      expect.objectContaining({ heading: 'Delete this exhibition?' }),
    );
    const deleted = http.expectOne(ONE);
    expect(deleted.request.method).toBe('DELETE');
    expect(deleted.request.headers.get('If-Match')).toBe(EXHIBITION.etag);
    deleted.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalled();
  });
});
