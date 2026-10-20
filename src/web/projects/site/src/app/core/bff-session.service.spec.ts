import {
  HttpXsrfTokenExtractor,
  provideHttpClient,
  withXsrfConfiguration,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BffSessionService } from './bff-session.service';

/** The fake backend of the current test, checked for unanswered requests afterwards. */
let controller: HttpTestingController | undefined;

/** A made-up test value, never a real credential. */
const PASSPHRASE = ['correct', 'horse', 'battery'].join('-');
const EMAIL = 'ada@6311arts.test';

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(
        withXsrfConfiguration({ cookieName: '__Host-6311.csrf', headerName: 'X-XSRF-TOKEN' }),
      ),
      provideHttpClientTesting(),
      { provide: HttpXsrfTokenExtractor, useValue: { getToken: () => 'csrf-1' } },
    ],
  });
  return {
    service: TestBed.inject(BffSessionService),
    http: (controller = TestBed.inject(HttpTestingController)),
  };
}

/** Lets the service's awaited steps run, so the next request has been sent. */
const settle = () => new Promise((resolve) => setTimeout(resolve));

const noContent = { status: 204, statusText: 'No Content' };

/** Answers the antiforgery fetch and hands back the login request that follows it. */
async function loginRequest(http: HttpTestingController) {
  http.expectOne('/bff/antiforgery').flush(null, noContent);
  await settle();
  return http.expectOne('/bff/login');
}

describe('BffSessionService', () => {
  afterEach(() => {
    controller?.verify();
    controller = undefined;
  });

  it('fetches the antiforgery pair, then posts the credentials with the header', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);

    const login = await loginRequest(http);
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({ email: EMAIL, password: PASSPHRASE });
    expect(login.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-1');
    login.flush({ displayName: 'Ada', roles: ['Admin'] });

    expect(await result).toEqual({
      kind: 'signed-in',
      session: { displayName: 'Ada', roles: ['Admin'] },
    });
  });

  it('never puts the password in a URL', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);

    const login = await loginRequest(http);
    expect(login.request.urlWithParams).not.toContain(PASSPHRASE);
    login.flush({ displayName: 'Ada', roles: ['Admin'] });
    await result;
  });

  it('gives one answer for every credential refusal', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);

    (await loginRequest(http)).flush(
      {
        title: 'Check your email address and password, then try again.',
        code: 'auth.credentials_invalid',
      },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(await result).toEqual({ kind: 'refused' });
  });

  it('reports a rate limit with the Retry-After it was given', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);

    (await loginRequest(http)).flush(
      { title: 'Too many requests.', code: 'request.rate_limited' },
      { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '240' } },
    );

    expect(await result).toEqual({ kind: 'rate-limited', retryAfterSeconds: 240 });
  });

  it('is unavailable when the gateway cannot reach Identity', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);

    (await loginRequest(http)).flush(
      { title: 'Sign-in is unavailable.', code: 'auth.identity_unavailable' },
      { status: 503, statusText: 'Unavailable' },
    );

    expect(await result).toEqual({ kind: 'unavailable' });
  });

  it('fetches a new pair and tries exactly once more when the token was stale', async () => {
    const { service, http } = setup();
    const result = service.signIn(EMAIL, PASSPHRASE);
    const stale = {
      title: 'This request needs a current antiforgery token.',
      code: 'auth.antiforgery_required',
    };
    const badRequest = { status: 400, statusText: 'Bad Request' };

    (await loginRequest(http)).flush(stale, badRequest);
    await settle();
    (await loginRequest(http)).flush(stale, badRequest);

    expect(await result).toEqual({ kind: 'refused' });
  });

  it('reads the current session, and treats a 401 as nobody', async () => {
    const { service, http } = setup();
    const signedIn = service.current();
    http.expectOne('/bff/session').flush({ displayName: 'Ada', roles: ['Editor'] });
    expect(await signedIn).toEqual({ displayName: 'Ada', roles: ['Editor'] });

    const nobody = service.current();
    http
      .expectOne('/bff/session')
      .flush({ title: 'Sign in to continue.' }, { status: 401, statusText: 'Unauthorized' });
    expect(await nobody).toBeNull();
  });
});
