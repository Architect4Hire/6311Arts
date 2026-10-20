import {
  HttpXsrfTokenExtractor,
  provideHttpClient,
  withXsrfConfiguration,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AccountService } from './account.service';

/** The fake backend of the current test, checked for unanswered requests afterwards. */
let controller: HttpTestingController | undefined;

/** Made-up test values, never real credentials. */
const LINK = { userId: 'user-1', token: 'CfDJ8-a-link-token' };
const NEW_PASSPHRASE = ['another', 'perfectly', 'fine', 'one'].join('-');

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
    service: TestBed.inject(AccountService),
    http: (controller = TestBed.inject(HttpTestingController)),
  };
}

const settle = () => new Promise((resolve) => setTimeout(resolve));
const noContent = { status: 204, statusText: 'No Content' };
const badRequest = { status: 400, statusText: 'Bad Request' };

/** Answers the antiforgery fetch and hands back the POST that follows it. */
async function post(http: HttpTestingController, url: string) {
  http.expectOne('/bff/antiforgery').flush(null, noContent);
  await settle();
  return http.expectOne(url);
}

describe('AccountService', () => {
  afterEach(() => {
    controller?.verify();
    controller = undefined;
  });

  it.each([
    ['password-reset', '/bff/forgot-password'],
    ['email-confirmation', '/bff/resend-confirmation'],
  ] as const)('asks for a %s email by address, with the antiforgery header', async (kind, url) => {
    const { service, http } = setup();
    const result = service.requestEmail(kind, 'ada@6311arts.test');

    const request = await post(http, url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ada@6311arts.test' });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-1');
    request.flush(null, { status: 202, statusText: 'Accepted' });

    expect(await result).toEqual({ kind: 'sent' });
  });

  it('reports a rate limit on a request with its Retry-After', async () => {
    const { service, http } = setup();
    const result = service.requestEmail('password-reset', 'ada@6311arts.test');

    (await post(http, '/bff/forgot-password')).flush(
      { code: 'request.rate_limited' },
      { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '120' } },
    );

    expect(await result).toEqual({ kind: 'rate-limited', retryAfterSeconds: 120 });
  });

  it('posts the link and the new password in the body, never the URL', async () => {
    const { service, http } = setup();
    const result = service.resetPassword(LINK, NEW_PASSPHRASE);

    const request = await post(http, '/bff/reset-password');
    expect(request.request.body).toEqual({ ...LINK, newPassword: NEW_PASSPHRASE });
    expect(request.request.urlWithParams).toBe('/bff/reset-password');
    request.flush(null, noContent);

    expect(await result).toEqual({ kind: 'done' });
  });

  it('passes on the password policy’s reasons', async () => {
    const { service, http } = setup();
    const result = service.resetPassword(LINK, 'short');

    (await post(http, '/bff/reset-password')).flush(
      {
        code: 'auth.password_rejected',
        errors: { newPassword: ['A staff password must be at least 14 characters.', 42] },
      },
      badRequest,
    );

    expect(await result).toEqual({
      kind: 'password-rejected',
      reasons: ['A staff password must be at least 14 characters.'],
    });
  });

  it.each(['auth.token_invalid', 'validation.failed'])(
    'treats %s as a link that cannot be used',
    async (code) => {
      const { service, http } = setup();
      const result = service.confirmEmail(LINK);

      (await post(http, '/bff/confirm-email')).flush({ code }, badRequest);

      expect(await result).toEqual({ kind: 'link-invalid' });
    },
  );

  it('never reports an outage as an expired link', async () => {
    const { service, http } = setup();
    const result = service.confirmEmail(LINK);

    (await post(http, '/bff/confirm-email')).flush(
      { code: 'auth.identity_unavailable' },
      { status: 503, statusText: 'Unavailable' },
    );

    expect(await result).toEqual({ kind: 'unavailable' });
  });

  it('keeps the link when the antiforgery pair is still refused after the retry', async () => {
    const { service, http } = setup();
    const result = service.confirmEmail(LINK);
    const stale = { code: 'auth.antiforgery_required' };

    (await post(http, '/bff/confirm-email')).flush(stale, badRequest);
    await settle();
    (await post(http, '/bff/confirm-email')).flush(stale, badRequest);

    // Not "link-invalid": that would make the page forget a link that still works.
    expect(await result).toEqual({ kind: 'unavailable' });
  });

  it('does not blame the address for a refused antiforgery pair', async () => {
    const { service, http } = setup();
    const result = service.requestEmail('password-reset', 'ada@6311arts.test');
    const stale = { code: 'auth.antiforgery_required' };

    (await post(http, '/bff/forgot-password')).flush(stale, badRequest);
    await settle();
    (await post(http, '/bff/forgot-password')).flush(stale, badRequest);

    expect(await result).toEqual({ kind: 'unavailable' });
  });

  it('fetches a new antiforgery pair and tries exactly once more when the token was stale', async () => {
    const { service, http } = setup();
    const result = service.confirmEmail(LINK);
    const stale = { code: 'auth.antiforgery_required' };

    (await post(http, '/bff/confirm-email')).flush(stale, badRequest);
    await settle();
    (await post(http, '/bff/confirm-email')).flush(null, noContent);

    expect(await result).toEqual({ kind: 'done' });
  });
});
