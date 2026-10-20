import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BrowserNavigation } from '../browser/browser-navigation';
import { SessionService } from './session.service';

/** The fake backend of the current test, checked for unanswered requests afterwards. */
let controller: HttpTestingController | undefined;

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const navigation = TestBed.inject(BrowserNavigation);
  const assign = vi.spyOn(navigation, 'assign').mockImplementation(() => undefined);
  vi.spyOn(navigation, 'currentPath').mockReturnValue('/admin/classes?draft=1');
  return {
    service: TestBed.inject(SessionService),
    http: (controller = TestBed.inject(HttpTestingController)),
    assign,
  };
}

describe('SessionService', () => {
  afterEach(() => {
    controller?.verify();
    controller = undefined;
  });

  it('starts loading and becomes signed-in with the name and roles /bff/session returns', async () => {
    const { service, http } = setup();
    expect(service.state()).toEqual({ status: 'loading' });

    const loaded = service.ensureLoaded();
    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Editor'] });

    expect(await loaded).toEqual({
      status: 'signed-in',
      session: { displayName: 'Ada', roles: ['Editor'] },
    });
    expect(service.isStaff()).toBe(true);
    expect(service.hasAnyRole(['Admin'])).toBe(false);
  });

  it('asks the gateway once however many callers are waiting', async () => {
    const { service, http } = setup();
    const first = service.ensureLoaded();
    const second = service.ensureLoaded();

    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Admin'] });

    expect(await first).toBe(await second);
    expect(await service.ensureLoaded()).toEqual(await first);
  });

  it('is signed-out on a 401', async () => {
    const { service, http } = setup();
    const loaded = service.ensureLoaded();
    http
      .expectOne('/bff/session')
      .flush(
        { title: 'Sign in to continue.', code: 'auth.unauthenticated' },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(await loaded).toEqual({ status: 'signed-out' });
    expect(service.session()).toBeNull();
  });

  it.each([
    ['a 503', { status: 503, statusText: 'Unavailable' }, { title: 'The service is unavailable.' }],
    ['a body that is not a session', { status: 200, statusText: 'OK' }, { token: 'x' }],
  ])('is unavailable on %s', async (_name, init, body) => {
    const { service, http } = setup();
    const loaded = service.ensureLoaded();
    http.expectOne('/bff/session').flush(body, init);

    expect(await loaded).toEqual({ status: 'unavailable' });
  });

  it('keeps nothing in browser storage', async () => {
    const { service, http } = setup();
    const loaded = service.ensureLoaded();
    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Admin'] });
    await loaded;

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('sends an expired session to sign-in, coming back to the same screen', () => {
    const { service, assign } = setup();
    service.expired();
    service.expired();

    expect(assign).toHaveBeenCalledTimes(1);
    expect(assign).toHaveBeenCalledWith('/signin?returnUrl=%2Fadmin%2Fclasses%3Fdraft%3D1');
    expect(service.state()).toEqual({ status: 'signed-out' });
  });

  it('signs out through /bff/logout and leaves for /signin', async () => {
    const { service, http, assign } = setup();
    const done = service.signOut();
    const request = http.expectOne('/bff/logout');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(assign).toHaveBeenCalledWith('/signin');
    expect(service.state()).toEqual({ status: 'signed-out' });
  });

  it('stays signed in when "sign out everywhere" fails', async () => {
    const { service, http, assign } = setup();
    const loaded = service.ensureLoaded();
    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Admin'] });
    await loaded;

    const done = service.signOutEverywhere();
    http
      .expectOne('/bff/logout-everywhere')
      .flush({ title: 'Sessions could not be ended.' }, { status: 503, statusText: 'Unavailable' });

    await expect(done).rejects.toBeTruthy();
    expect(assign).not.toHaveBeenCalled();
    expect(service.state().status).toBe('signed-in');
  });
});
