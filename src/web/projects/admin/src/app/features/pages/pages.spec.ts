import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { Page } from '../../models/page';
import { PAGE, toPageDeletedRow, toPageSummary } from '../../models/page.fixtures';
import { RichTextEditor } from '../../shared/rich-text-editor/rich-text-editor';
import { PageEditor } from './page-editor/page-editor';
import { PageList } from './page-list/page-list';

const BASE = '/api/v1/cms/pages';
const ONE = `${BASE}/${PAGE.id}`;

// ProseMirror measures the selection to scroll it into view; jsdom has no layout, so ranges answer with nothing.
Range.prototype.getClientRects ??= () =>
  ({
    length: 0,
    item: () => null,
    [Symbol.iterator]: [][Symbol.iterator],
  }) as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();
document.elementFromPoint ??= () => null;

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

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label);
}

function input(root: HTMLElement, name: string): HTMLInputElement {
  return root.querySelector(`[name="${name}"]`) as HTMLInputElement;
}

function type(root: HTMLElement, name: string, value: string) {
  const control = input(root, name);
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

async function openEditor(page: Page | null, roles: string[] = ['Editor']) {
  const context = await configure(roles);
  const fixture = TestBed.createComponent(PageEditor);
  if (page) fixture.componentRef.setInput('id', page.id);
  fixture.detectChanges();
  if (page) {
    context.http.expectOne(`${BASE}/${page.id}`).flush(page);
    await settle(fixture);
  }
  await settle(fixture);
  const body = () =>
    fixture.debugElement.query(By.directive(RichTextEditor)).componentInstance as RichTextEditor;
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement, body };
}

describe('PageList', () => {
  it('lists pages by title with their status and links to each', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(PageList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement.querySelector('[role="status"]'))).toBe('Loading pages…');
    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          toPageSummary(PAGE),
          { ...toPageSummary(PAGE), id: 'b', title: 'Terms and Conditions', slug: 'terms' },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows.map((row) => text(row.querySelector('th a')))).toEqual([
      'Privacy Policy',
      'Terms and Conditions',
    ]);
    expect(text(rows[0])).toContain('/privacy');
    expect(text(rows[0])).toContain('Draft');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(`/pages/${PAGE.id}`);
  });

  it('offers a retry when the pages cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. The component had all four;
    // only the happy path was tested, so a 503 leaving "Loading pages…" on screen for good would have passed
    // (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(PageList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading pages…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The pages couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });

  it('shows Deleted to Admins only, and restores a page from it', async () => {
    // ADR 0052: the Deleted view and the restore are the Admin's, like the delete itself.
    const { http } = await configure();
    const asEditor = TestBed.createComponent(PageList);
    asEditor.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(asEditor);

    expect(button(asEditor.nativeElement, 'Deleted')).toBeUndefined();
    TestBed.resetTestingModule();

    const admin = await configure(['Admin']);
    const fixture = TestBed.createComponent(PageList);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    admin.http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    button(root, 'Deleted')!.click();
    await settle(fixture);

    // The live list is not fetched behind the Deleted view: it would announce its own count over this one.
    admin.http.expectNone((r) => r.url === BASE);

    const deleted = admin.http.expectOne((r) => r.url === `${BASE}/deleted`);
    expect(deleted.request.params.has('status')).toBe(false);
    deleted.flush({ items: [toPageDeletedRow(PAGE)], nextCursor: null });
    await settle(fixture);

    // No link: there is no editor to open for a page nothing else can see.
    expect(root.querySelector('tbody th a')).toBeNull();
    expect(text(root.querySelector('tbody tr'))).toContain('Privacy Policy');

    const ask = vi.spyOn(admin.confirm, 'confirm').mockResolvedValue(true);
    button(root, 'Restore')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(
      expect.objectContaining({ heading: 'Restore “Privacy Policy”?' }),
    );
    const restore = admin.http.expectOne(`${ONE}/restore`);
    expect(restore.request.headers.get('If-Match')).toBe(PAGE.etag);
    restore.flush(PAGE);
    await settle(fixture);

    admin.http.expectOne((r) => r.url === `${BASE}/deleted`).flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(root)).toContain('No page has been deleted.');
  });

  it('says so when a restore is refused because the address was taken', async () => {
    const { http, confirm } = await configure(['Admin']);
    const fixture = TestBed.createComponent(PageList);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    button(root, 'Deleted')!.click();
    await settle(fixture);
    http
      .expectOne((r) => r.url === `${BASE}/deleted`)
      .flush({ items: [toPageDeletedRow(PAGE)], nextCursor: null });
    await settle(fixture);

    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    button(root, 'Restore')!.click();
    await settle(fixture);
    http
      .expectOne(`${ONE}/restore`)
      .flush({ code: 'cms.slug_taken' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(text(root)).toContain('couldn’t be restored');
    expect(text(root)).toContain('/privacy');
  });
});

describe('PageEditor', () => {
  it('offers a retry when the page cannot be loaded', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(PageEditor);
    fixture.componentRef.setInput('id', PAGE.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This page couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne(ONE).flush(PAGE);
    await settle(fixture);

    expect(input(fixture.nativeElement, 'title').value).toBe(PAGE.title);
  });

  it('says there is no such page on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(PageEditor);
    fixture.componentRef.setInput('id', PAGE.id);
    fixture.detectChanges();

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such page');
    expect(root.querySelector('a[href="/pages"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('shows the stored body untouched, then creates a page with its title, address and text', async () => {
    const { http, fixture, root, router, body } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'About Us');
    await settle(fixture);
    expect(input(root, 'slug').value).toBe('about-us');
    expect(input(root, 'slug').disabled).toBe(true);

    body().value.set('<p>We are an art center.</p>');
    await settle(fixture);
    button(root, 'Create page')!.click();

    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({
      title: 'About Us',
      body: '<p>We are an art center.</p>',
    });
    create.flush(
      { ...PAGE, title: 'About Us', slug: 'about-us' },
      { status: 201, statusText: 'Created' },
    );
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/pages', PAGE.id], {
      replaceUrl: true,
      state: { pageCreated: true },
    });
  });

  it('is not dirty just for loading a body the editor would write differently', async () => {
    const { fixture } = await openEditor({ ...PAGE, body: '<ul><li>One</li></ul>' });

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('sends only the body when only the body changed, and shows a body refusal on the text', async () => {
    const { http, fixture, root, body } = await openEditor(PAGE);

    body().value.set('<p>New text.</p>');
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);
    expect(patch.request.headers.get('If-Match')).toBe(PAGE.etag);
    expect(patch.request.body).toEqual({ body: '<p>New text.</p>' });
    patch.flush(
      {
        code: 'validation.failed',
        detail: 'The page’s text needs attention.',
        errors: { body: ['A picture has no alt text. Describe it for people who can’t see it.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(root.querySelector('[role="textbox"]')?.getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('A picture has no alt text.');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('on a 409 shows both versions as words and can keep the editor’s changes', async () => {
    const { http, fixture, root, body } = await openEditor(PAGE);
    const theirs: Page = {
      ...PAGE,
      body: '<p>Their <strong>policy</strong>.</p>',
      etag: '"theirs"',
    };

    body().value.set('<p>My policy.</p>');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    const comparison = text(root.querySelector('.compare'));
    expect(comparison).toContain('My policy.');
    expect(comparison).toContain('Their policy.');
    expect(comparison).not.toContain('<strong>');

    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    expect(retry.request.body).toEqual({ body: '<p>My policy.</p>' });
  });

  it('says why a publish was refused, beside the text', async () => {
    const { http, fixture, root } = await openEditor({ ...PAGE, body: null });

    button(root, 'Publish')!.click();
    http.expectOne(`${ONE}/publish`).flush(
      {
        code: 'validation.failed',
        detail: 'This page isn’t ready to publish yet.',
        errors: {
          body: ['Write the page first: a published page with no text would be empty on the site.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);

    expect(text(root)).toContain('This page isn’t ready to publish yet.');
    expect(root.querySelector('[role="textbox"]')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('keeps the web address as it is whatever the title becomes, and offers archive to Admins only', async () => {
    const published: Page = {
      ...PAGE,
      status: 'Published',
      slugLocked: true,
      publishedAt: PAGE.createdAt,
    };
    const editor = await openEditor(published);
    expect(input(editor.root, 'slug').disabled).toBe(true);
    expect(text(editor.root)).toContain('6311arts.com/privacy');
    type(editor.root, 'title', 'Privacy Policy');
    await settle(editor.fixture);
    expect(input(editor.root, 'slug').value).toBe('privacy');
    expect(button(editor.root, 'Archive')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm } = await openEditor(published, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    button(root, 'Archive')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Archive this page?' }));
    http.expectOne(`${ONE}/archive`).flush({ ...published, status: 'Archived', etag: '"v2"' });
    await settle(fixture);
    expect(text(root.querySelector('header'))).toContain('Archived');
  });

  it('offers delete to Admins only, warns what it does, and returns to the list', async () => {
    // ADR 0052: Admin only, and offered from any status — including Published, which the dialog says will stop
    // working.
    const published: Page = {
      ...PAGE,
      status: 'Published',
      slugLocked: true,
      publishedAt: PAGE.createdAt,
    };
    const editor = await openEditor(published);
    expect(button(editor.root, 'Delete page')).toBeUndefined();
    TestBed.resetTestingModule();

    const { http, fixture, root, confirm, router } = await openEditor(published, ['Admin']);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    button(root, 'Delete page')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(
      expect.objectContaining({
        heading: 'Delete this page?',
        message: expect.stringContaining('6311arts.com/privacy stops working'),
      }),
    );
    const deleted = http.expectOne(ONE);
    expect(deleted.request.method).toBe('DELETE');
    expect(deleted.request.headers.get('If-Match')).toBe(published.etag);
    deleted.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/pages'], {
      state: { pageDeleted: 'Privacy Policy' },
    });
  });

  it('keeps the page when the delete dialog is cancelled', async () => {
    const { http, fixture, root, confirm } = await openEditor(PAGE, ['Admin']);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(false);

    button(root, 'Delete page')!.click();
    await settle(fixture);

    // Nothing was sent, and the editor is still showing the page.
    http.expectNone(ONE);
    expect(button(root, 'Delete page')).toBeDefined();
  });

  it('keeps the page and says why when the delete is refused', async () => {
    const { http, fixture, root, confirm } = await openEditor(PAGE, ['Admin']);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);

    button(root, 'Delete page')!.click();
    await settle(fixture);
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(text(root)).toContain('Someone else changed this page a moment ago');
    expect(button(root, 'Delete page')).toBeDefined();
  });

  it('schedules the page and offers a preview link', async () => {
    const { http, fixture, root } = await openEditor(PAGE);

    type(root, 'scheduleDate', '2026-12-24');
    type(root, 'scheduleTime', '17:00');
    await settle(fixture);
    button(root, 'Schedule')!.click();

    const schedule = http.expectOne(`${ONE}/schedule`);
    expect(schedule.request.headers.get('If-Match')).toBe(PAGE.etag);
    expect(schedule.request.body).toEqual({ scheduledFor: '2026-12-24T23:00:00+00:00' });
    schedule.flush({
      ...PAGE,
      status: 'Scheduled',
      scheduledFor: '2026-12-24T23:00:00+00:00',
      etag: '"v2"',
    });
    await settle(fixture);
    expect(text(root)).toContain('Publishing on');

    button(root, 'Preview')!.click();
    const minted = http.expectOne('/api/v1/cms/preview');
    expect(minted.request.body).toEqual({ entityType: 'page', entityId: PAGE.id });
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
});
