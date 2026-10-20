import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { APP_BASE_HREF } from '@angular/common';
import { BrowserNavigation } from '../browser/browser-navigation';
import { ConfirmService } from '../confirm/confirm.service';
import { SessionService } from '../session/session.service';
import { roleGuard } from './role.guard';
import { sessionGuard } from './session.guard';
import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

@Component({ template: '' })
class Blank {}

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: Blank }]),
      { provide: APP_BASE_HREF, useValue: '/admin/' },
    ],
  });
  const navigation = TestBed.inject(BrowserNavigation);
  return {
    http: TestBed.inject(HttpTestingController),
    router: TestBed.inject(Router),
    session: TestBed.inject(SessionService),
    assign: vi.spyOn(navigation, 'assign').mockImplementation(() => undefined),
  };
}

const route = {} as ActivatedRouteSnapshot;
const state = (url: string) => ({ url }) as RouterStateSnapshot;

function runSessionGuard(url: string) {
  return TestBed.runInInjectionContext(() => sessionGuard(route, state(url))) as Promise<
    boolean | UrlTree
  >;
}

async function signIn(http: HttpTestingController, session: SessionService, roles: string[]) {
  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada', roles });
  await loaded;
}

describe('sessionGuard', () => {
  it.each([['Admin'], ['Editor']])('lets a %s session in', async (role) => {
    const { http, session } = setup();
    await signIn(http, session, [role]);

    expect(await runSessionGuard('/classes')).toBe(true);
  });

  it('sends a signed-out visit to /signin with the full CMS path to come back to', async () => {
    const { http, assign } = setup();
    const result = runSessionGuard('/classes?draft=1');
    http
      .expectOne('/bff/session')
      .flush({ title: 'Sign in to continue.' }, { status: 401, statusText: 'Unauthorized' });

    expect(await result).toBe(false);
    expect(assign).toHaveBeenCalledWith('/signin?returnUrl=%2Fadmin%2Fclasses%3Fdraft%3D1');
  });

  it('shows a member session the "not available" page, never the sign-in page', async () => {
    const { http, session, router, assign } = setup();
    await signIn(http, session, ['Member']);

    const result = await runSessionGuard('/classes');
    expect(router.serializeUrl(result as UrlTree)).toBe('/forbidden');
    expect(assign).not.toHaveBeenCalled();
  });

  it('shows the "unavailable" page when the gateway cannot say', async () => {
    const { http, router } = setup();
    const result = runSessionGuard('/media');
    http
      .expectOne('/bff/session')
      .flush({ title: 'The service is unavailable.' }, { status: 503, statusText: 'Unavailable' });

    expect(router.serializeUrl((await result) as UrlTree)).toBe('/unavailable?from=%2Fmedia');
  });
});

describe('roleGuard', () => {
  it('lets the listed role in and turns the others away to /forbidden', async () => {
    const { http, session, router } = setup();
    await signIn(http, session, ['Editor']);

    const adminOnly = TestBed.runInInjectionContext(() =>
      roleGuard('Admin')(route, state('/sync')),
    );
    const staff = TestBed.runInInjectionContext(() =>
      roleGuard('Admin', 'Editor')(route, state('/classes')),
    );

    expect(router.serializeUrl(adminOnly as UrlTree)).toBe('/forbidden');
    expect(staff).toBe(true);
  });
});

describe('unsavedChangesGuard', () => {
  const screen = (dirty: boolean): HasUnsavedChanges => ({ hasUnsavedChanges: () => dirty });
  const run = (component: HasUnsavedChanges) =>
    TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(component, route, state('/pages/1'), state('/media')),
    );

  it('lets a clean screen go without asking', () => {
    setup();
    const confirm = vi.spyOn(TestBed.inject(ConfirmService), 'confirm');

    expect(run(screen(false))).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
  });

  it.each([
    [true, true],
    [false, false],
  ])('asks before leaving unsaved edits; confirm=%s → leave=%s', async (answer, leaves) => {
    setup();
    const confirm = TestBed.inject(ConfirmService);

    const result = run(screen(true)) as Promise<boolean>;
    expect(confirm.request()).toMatchObject({
      heading: 'Discard unsaved changes?',
      confirmLabel: 'Discard changes',
      cancelLabel: 'Keep editing',
    });
    confirm.answer(answer);

    expect(await result).toBe(leaves);
    expect(confirm.request()).toBeNull();
  });
});
