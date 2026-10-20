import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { Artist } from '../../models/artist';
import { ARTIST, toArtistSummary } from '../../models/artist.fixtures';
import { ASSET } from '../../models/media.fixtures';
import { ArtistEditor } from './artist-editor';
import { ArtistList } from './artist-list';

const BASE = '/api/v1/cms/artists';
const ONE = `${BASE}/${ARTIST.id}`;

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

function buttons(root: HTMLElement): HTMLButtonElement[] {
  return [...root.querySelectorAll('button')];
}

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return buttons(root).find((candidate) => text(candidate) === label);
}

function input(root: HTMLElement, name: string): HTMLInputElement | HTMLTextAreaElement {
  return root.querySelector(`[name="${name}"]`) as HTMLInputElement | HTMLTextAreaElement;
}

function type(root: HTMLElement, name: string, value: string) {
  const control = input(root, name);
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

async function openEditor(artist: Artist | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(ArtistEditor);
  if (artist) fixture.componentRef.setInput('id', artist.id);
  fixture.detectChanges();
  if (artist) {
    context.http.expectOne(`${BASE}/${artist.id}`).flush(artist);
    await settle(fixture);
  }
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ArtistList', () => {
  it('shows the roster with each status in words and links to each artist', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ArtistList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe('Loading artists…');
    const request = http.expectOne((r) => r.url === BASE);
    expect(request.request.params.get('status')).toBeNull();
    request.flush({
      items: [
        toArtistSummary(ARTIST),
        { ...toArtistSummary(ARTIST), id: 'b', name: 'Piloto', status: 'Published' },
      ],
      nextCursor: 'next',
    });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual(['Carrie Haase', 'Piloto']);
    expect(text(rows[0])).toContain('Draft');
    expect(text(rows[1])).toContain('Published');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(`/artists/${ARTIST.id}`);
    expect(button(fixture.nativeElement, 'Load more')).toBeTruthy();
  });

  it('asks again with the status chosen, and says when nothing matches', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ArtistList);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === BASE)
      .flush({ items: [toArtistSummary(ARTIST)], nextCursor: null });
    await settle(fixture);

    button(fixture.nativeElement, 'Archived')!.click();
    fixture.detectChanges();
    const filtered = http.expectOne((r) => r.url === BASE);
    expect(filtered.request.params.get('status')).toBe('Archived');
    filtered.flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('No artist is archived.');
  });

  it('searches after a pause in typing, not on every key', async () => {
    vi.useFakeTimers();
    try {
      const { http } = await configure();
      const fixture = TestBed.createComponent(ArtistList);
      fixture.detectChanges();
      http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });

      type(fixture.nativeElement, 'search', 'ha');
      type(fixture.nativeElement, 'search', 'haase');
      http.expectNone((r) => r.url === BASE);

      vi.advanceTimersByTime(300);
      fixture.detectChanges();
      const searched = http.expectOne((r) => r.url === BASE);
      expect(searched.request.params.get('search')).toBe('haase');
    } finally {
      vi.useRealTimers();
    }
  });

  it('keeps the rows on screen while a new filter loads, and says how many came back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ArtistList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [toArtistSummary(ARTIST)], nextCursor: null });
    await settle(fixture);

    button(fixture.nativeElement, 'Drafts')!.click();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(root.querySelector('table')?.getAttribute('aria-busy')).toBe('true');

    http.expectOne((r) => r.url === BASE).flush({ items: [toArtistSummary(ARTIST)], nextCursor: null });
    await settle(fixture);
    expect(root.querySelector('table')?.getAttribute('aria-busy')).toBeNull();
    expect(text(root.querySelector('[aria-live="polite"]'))).toBe('1 artist.');
  });

  it('offers a retry when the roster cannot be loaded', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(ArtistList);
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The artists couldn’t be loaded');
    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE);
  });
});

describe('ArtistEditor', () => {
  it('suggests the web address from the name until one is typed, then creates and moves to the artist', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'name', 'Socorro Muciño');
    await settle(fixture);
    expect(input(root, 'slug').value).toBe('socorro-mucino');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);

    type(root, 'slug', 'socorro');
    type(root, 'name', 'Socorro M.');
    await settle(fixture);
    expect(input(root, 'slug').value).toBe('socorro');

    button(root, 'Create artist')!.click();
    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({ name: 'Socorro M.', slug: 'socorro', active: true });
    create.flush(
      { ...ARTIST, name: 'Socorro M.', slug: 'socorro' },
      { status: 201, statusText: 'Created' },
    );
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/artists', ARTIST.id], {
      replaceUrl: true,
      state: { artistCreated: true },
    });
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('holds the form still while a save is in flight, so nothing typed then is lost', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);

    type(root, 'bio', 'New bio.');
    await settle(fixture);
    button(root, 'Save')!.click();
    await settle(fixture);

    expect(root.querySelector('fieldset')?.disabled).toBe(true);
    http.expectOne(ONE).flush({ ...ARTIST, bio: 'New bio.', etag: '"v2"' });
    await settle(fixture);
    expect(root.querySelector('fieldset')?.disabled).toBe(false);
  });

  it('refuses an empty name before asking the API', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'medium', 'Ink');
    await settle(fixture);
    button(root, 'Create artist')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(input(root, 'name').getAttribute('aria-invalid')).toBe('true');
  });

  it('sends only what changed, under the ETag it read', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);

    type(root, 'medium', '');
    type(root, 'bio', 'New bio.');
    await settle(fixture);
    button(root, 'Save')!.click();

    const patch = http.expectOne(ONE);
    expect(patch.request.headers.get('If-Match')).toBe(ARTIST.etag);
    expect(patch.request.body).toEqual({ medium: '', bio: 'New bio.' });
    patch.flush({ ...ARTIST, medium: null, bio: 'New bio.', etag: '"v2"' });
    await settle(fixture);

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('shows a taken web address beside its field and keeps what was typed', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);

    type(root, 'slug', 'piloto');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush(
        {
          code: 'cms.slug_taken',
          detail: 'Another artist already has this web address.',
          errors: { slug: ['“piloto” is taken. Choose another.'] },
        },
        { status: 409, statusText: 'Conflict' },
      );
    await settle(fixture);

    expect(input(root, 'slug').value).toBe('piloto');
    expect(input(root, 'slug').getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('“piloto” is taken. Choose another.');
  });

  it('on a 409 shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);
    const theirs: Artist = { ...ARTIST, medium: 'Metalsmithing', etag: '"theirs"' };

    type(root, 'medium', 'Ceramics');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = root.querySelector('.compare')!;
    expect(text(comparison)).toContain('Ceramics');
    expect(text(comparison)).toContain('Metalsmithing');
    expect(input(root, 'medium').value).toBe('Ceramics');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    expect(retry.request.body).toEqual({ medium: 'Ceramics' });
  });

  it('using the saved version after a 409 says so and puts focus on the title', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);
    const theirs: Artist = { ...ARTIST, medium: 'Metalsmithing', etag: '"theirs"' };

    type(root, 'medium', 'Ceramics');
    await settle(fixture);
    button(root, 'Save')!.click();
    http.expectOne(ONE).flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    button(root, 'Use the saved version and discard mine')!.click();
    await settle(fixture);

    expect(input(root, 'medium').value).toBe('Metalsmithing');
    expect(document.activeElement).toBe(root.querySelector('h1'));
    expect(text(root.querySelector('[aria-live="polite"]'))).toContain('Showing the saved version');
  });

  it('locks the web address once published', async () => {
    const { fixture, root } = await openEditor({
      ...ARTIST,
      status: 'Published',
      slugLocked: true,
      publishedAt: ARTIST.createdAt,
    });
    await settle(fixture);

    expect(input(root, 'slug').hasAttribute('readonly')).toBe(true);
    expect(text(root)).toContain('fixed now the artist has been published');
  });

  it('publishes the saved version, and waits while there are unsaved edits', async () => {
    const { http, fixture, root } = await openEditor(ARTIST);

    type(root, 'medium', 'Ceramics');
    await settle(fixture);
    expect(button(root, 'Publish')!.disabled).toBe(true);
    expect(text(root)).toContain('Save your changes first');

    type(root, 'medium', ARTIST.medium!);
    await settle(fixture);
    button(root, 'Publish')!.click();
    const publish = http.expectOne(`${ONE}/publish`);
    const heading = () => root.querySelector('#publishing-heading');
    expect(publish.request.headers.get('If-Match')).toBe(ARTIST.etag);
    publish.flush({ ...ARTIST, status: 'Published', slugLocked: true, etag: '"v2"' });
    await settle(fixture);

    expect(button(root, 'Unpublish')).toBeTruthy();
    expect(button(root, 'Publish')).toBeUndefined();
    // The Publish button is gone, so focus moved to the panel rather than being dropped.
    expect(document.activeElement).toBe(heading());
  });

  it('says why a publish was refused for the portrait’s missing alt text', async () => {
    const withPortrait = { ...ARTIST, portraitMediaId: ASSET.id };
    const { http, fixture, root } = await openEditor(withPortrait);
    http.expectOne(`/api/v1/cms/media/${ASSET.id}`).flush({ ...ASSET, altText: null });
    await settle(fixture);
    expect(text(root)).toContain('This image has no alt text yet');

    button(root, 'Publish')!.click();
    http.expectOne(`${ONE}/publish`).flush(
      {
        code: 'validation.failed',
        detail: 'The portrait needs alt text before this artist can be published.',
        errors: { portraitMediaId: ['Add alt text to the portrait in Media, then publish.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root)).toContain(
      'The portrait needs alt text before this artist can be published.',
    );
    expect(text(root.querySelector('.panel .error'))).toBe(
      'Add alt text to the portrait in Media, then publish.',
    );
  });

  it('offers archive to Admins only, and asks first', async () => {
    const editor = await openEditor(ARTIST);
    expect(button(editor.root, 'Archive')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm } = await openEditor(ARTIST, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    button(root, 'Archive')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Archive this artist?' }));
    http.expectOne(`${ONE}/archive`).flush({ ...ARTIST, status: 'Archived', etag: '"v2"' });
    await settle(fixture);
    expect(text(root.querySelector('header'))).toContain('Archived');
  });

  it('says plainly when the artist does not exist', async () => {
    const { http, fixture, root } = await openEditor(null);
    fixture.componentRef.setInput('id', 'missing');
    fixture.detectChanges();
    http
      .expectOne(`${BASE}/missing`)
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(text(root)).toContain('There’s no such artist');
  });
});
