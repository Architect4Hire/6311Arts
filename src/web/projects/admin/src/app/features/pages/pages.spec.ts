import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { Page } from '../../models/page';
import { PAGE, toPageSummary } from '../../models/page.fixtures';
import { RichTextEditor } from '../../shared/rich-text-editor';
import { PageEditor } from './page-editor';
import { PageList } from './page-list';

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
});

describe('PageEditor', () => {
  it('shows the stored body untouched, then creates a page with its title, address and text', async () => {
    const { http, fixture, root, router, body } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'title', 'About Us');
    await settle(fixture);
    expect(input(root, 'slug').value).toBe('about-us');

    body().value.set('<p>We are an art center.</p>');
    await settle(fixture);
    button(root, 'Create page')!.click();

    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({
      title: 'About Us',
      slug: 'about-us',
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

  it('locks the web address once published, and offers archive to Admins only', async () => {
    const published: Page = {
      ...PAGE,
      status: 'Published',
      slugLocked: true,
      publishedAt: PAGE.createdAt,
    };
    const editor = await openEditor(published);
    // ADR 0035: a published address may change; the old one is redirected, and the hint says which.
    expect(input(editor.root, 'slug').hasAttribute('readonly')).toBe(false);
    expect(text(editor.root)).toContain('published, so changing it sends visitors');
    type(editor.root, 'slug', 'privacy-policy');
    await settle(editor.fixture);
    expect(text(editor.root)).toContain('visitors to /privacy are sent here');
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
