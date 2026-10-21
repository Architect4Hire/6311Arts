import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { vi } from 'vitest';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { Redirect, decodeRedirect, decodeRedirectList } from '../../models/redirect';
import { RedirectService } from '../../services/redirect.service';
import { RedirectEditor } from './redirect-editor/redirect-editor';
import { RedirectList } from './redirect-list/redirect-list';

const BASE = '/api/v1/cms/redirects';

const REDIRECT: Redirect = {
  id: '0199f0a0-0000-7000-8000-0000000000b1',
  fromPath: '/gallery',
  toPath: '/exhibitions',
  statusCode: 301,
  source: 'Imported',
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'system:redirect-seed',
  etag: '"0123456789abcdef0123456789abcdef"',
};
const ONE = `${BASE}/${REDIRECT.id}`;

async function configure() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Admin'] });
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

async function openEditor(redirect: Redirect | null) {
  const context = await configure();
  const fixture = TestBed.createComponent(RedirectEditor);
  if (redirect) fixture.componentRef.setInput('id', redirect.id);
  fixture.detectChanges();
  if (redirect) {
    context.http.expectOne(`${BASE}/${redirect.id}`).flush(redirect);
    await settle(fixture);
  }
  return { ...context, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('redirect models and service', () => {
  it('decodes exactly the API shape, the int status as a number or numeric string', () => {
    expect(decodeRedirect(REDIRECT)).toEqual(REDIRECT);
    expect(decodeRedirect({ ...REDIRECT, statusCode: '302' })?.statusCode).toBe(302);
    expect(decodeRedirect({ ...REDIRECT, source: 'Robot' })).toBeNull();
    expect(decodeRedirectList({ items: [REDIRECT, {}], nextCursor: null })).toBeNull();
  });

  it('sends a delete under the ETag it was given', async () => {
    const { http } = await configure();
    const service = TestBed.inject(RedirectService);

    const done = firstValueFrom(service.delete(REDIRECT.id, REDIRECT.etag));
    const request = http.expectOne(ONE);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('If-Match')).toBe(REDIRECT.etag);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });
});

describe('RedirectList', () => {
  it('lists each old address, where it leads and who made it', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(RedirectList);
    fixture.detectChanges();

    http
      .expectOne((r) => r.url === BASE)
      .flush({
        items: [
          REDIRECT,
          {
            ...REDIRECT,
            id: 'b',
            fromPath: '/artists/jane',
            toPath: '/artists/jane-doe',
            source: 'SlugChange',
          },
        ],
        nextCursor: null,
      });
    await settle(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')].map(
      (row) => text(row),
    );
    expect(rows[0]).toContain('/gallery');
    expect(rows[0]).toContain('/exhibitions');
    expect(rows[0]).toContain('From the old site');
    expect(rows[1]).toContain('Address change');
  });

  it('offers a retry when the redirects cannot be loaded, and an empty state when there are none', async () => {
    // frontend.md: every data surface has loading, empty, error and success states. All four existed in the
    // component and only the happy path was tested, so a 503 leaving "Loading redirects…" on screen for good
    // would have passed (test-gap review).
    const { http } = await configure();
    const fixture = TestBed.createComponent(RedirectList);
    fixture.detectChanges();

    expect(text(fixture.nativeElement)).toContain('Loading redirects…');

    http.expectOne((r) => r.url === BASE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('The redirects couldn’t be loaded');

    button(fixture.nativeElement, 'Try again')!.click();
    http.expectOne((r) => r.url === BASE).flush({ items: [], nextCursor: null });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('ui-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
  });
});

describe('RedirectEditor', () => {
  it('offers a retry when the record cannot be loaded, and asks again when it is used', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(RedirectEditor);
    fixture.componentRef.setInput('id', REDIRECT.id);
    fixture.detectChanges();

    http.expectOne(ONE).flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(text(fixture.nativeElement)).toContain('This redirect couldn’t be loaded');

    // The retry asks again rather than showing a form built from nothing.
    button(fixture.nativeElement, 'Try again')!.click();
    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    expect(text(fixture.nativeElement)).not.toContain('This redirect couldn’t be loaded');
  });

  it('says there is no such record on a 404, and offers the way back', async () => {
    const { http } = await configure();
    const fixture = TestBed.createComponent(RedirectEditor);
    fixture.componentRef.setInput('id', REDIRECT.id);
    fixture.detectChanges();

    http
      .expectOne(ONE)
      .flush(
        { code: 'resource.not_found', title: 'Not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(text(root)).toContain('There’s no such redirect');
    expect(root.querySelector('a[href="/redirects"]')).not.toBeNull();
    expect(button(root, 'Try again')).toBeUndefined();
  });

  it('creates a redirect and moves to it', async () => {
    const { http, fixture, root, router } = await openEditor(null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    type(root, 'fromPath', '/Old-Shop/');
    type(root, 'toPath', 'https://squareup.com/store/6311arts');
    await settle(fixture);
    button(root, 'Create redirect')!.click();

    const create = http.expectOne(BASE);
    expect(create.request.body).toEqual({
      fromPath: '/Old-Shop/',
      toPath: 'https://squareup.com/store/6311arts',
      statusCode: 301,
    });
    create.flush(
      {
        ...REDIRECT,
        fromPath: '/old-shop',
        toPath: 'https://squareup.com/store/6311arts',
        source: 'Manual',
      },
      { status: 201, statusText: 'Created' },
    );
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/redirects', REDIRECT.id], {
      replaceUrl: true,
      state: { redirectCreated: true },
    });
  });

  it('refuses an address that does not start with / before asking', async () => {
    const { http, fixture, root } = await openEditor(null);

    type(root, 'fromPath', 'gallery');
    type(root, 'toPath', '/exhibitions');
    await settle(fixture);
    button(root, 'Create redirect')!.click();
    await settle(fixture);

    http.expectNone(BASE);
    expect(input(root, 'fromPath').getAttribute('aria-invalid')).toBe('true');
  });

  it('shows a chain the API refused beside the field it is about, keeping what was typed', async () => {
    const { http, fixture, root } = await openEditor(REDIRECT);

    type(root, 'toPath', '/events');
    await settle(fixture);
    button(root, 'Save')!.click();
    const patch = http.expectOne(ONE);
    expect(patch.request.body).toEqual({ toPath: '/events' });
    expect(patch.request.headers.get('If-Match')).toBe(REDIRECT.etag);
    patch.flush(
      {
        code: 'redirects.loop_or_chain',
        detail:
          'Redirects go straight to their destination: no loops, and no chains of more than one hop.',
        errors: {
          toPath: ['/events already redirects to /classes. Send visitors straight there instead.'],
        },
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);

    expect(input(root, 'toPath').value).toBe('/events');
    expect(input(root, 'toPath').getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('/events already redirects to /classes.');
  });

  it('on a 409 conflict shows both versions and can keep the editor’s changes', async () => {
    const { http, fixture, root } = await openEditor(REDIRECT);
    const theirs: Redirect = { ...REDIRECT, toPath: '/exhibitions#current', etag: '"theirs"' };

    type(root, 'toPath', '/about');
    await settle(fixture);
    button(root, 'Save')!.click();
    http
      .expectOne(ONE)
      .flush({ code: 'concurrency.conflict' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne(ONE).flush(theirs);
    await settle(fixture);

    expect(text(root.querySelector('.compare'))).toContain('/exhibitions#current');
    button(root, 'Keep my changes')!.click();
    const retry = http.expectOne(ONE);
    expect(retry.request.headers.get('If-Match')).toBe('"theirs"');
    expect(retry.request.body).toEqual({ toPath: '/about' });
  });

  it('asks before deleting, deletes under the ETag and goes back to the list', async () => {
    const { http, fixture, root, confirm, router } = await openEditor(REDIRECT);
    const ask = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    button(root, 'Delete redirect')!.click();
    await settle(fixture);

    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ heading: 'Delete this redirect?' }));
    const request = http.expectOne(ONE);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('If-Match')).toBe(REDIRECT.etag);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith(['/redirects']);
  });
});
