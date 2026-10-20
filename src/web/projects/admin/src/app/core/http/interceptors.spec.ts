import {
  HttpClient,
  HttpContext,
  HttpXsrfTokenExtractor,
  provideHttpClient,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { BrowserNavigation } from '../browser/browser-navigation';
import { ToastService } from '../toast/toast.service';
import { antiforgeryInterceptor } from './antiforgery.interceptor';
import { SESSION_PROBE, SUPPRESS_PROBLEM_TOAST } from './http-context';
import { problemInterceptor } from './problem.interceptor';
import { XSRF_COOKIE_NAME, XSRF_HEADER_NAME } from './xsrf';

const refusal = {
  title: 'This request needs a current antiforgery token.',
  code: 'auth.antiforgery_required',
};

/** The interceptor stack exactly as app.config.ts builds it, over a fake backend and a token we control. */
/** The fake backend of the current test, checked for unanswered requests afterwards. */
let controller: HttpTestingController | undefined;

function setup(token: { value: string | null } = { value: 'token-1' }) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(
        withXsrfConfiguration({ cookieName: XSRF_COOKIE_NAME, headerName: XSRF_HEADER_NAME }),
        withInterceptors([problemInterceptor, antiforgeryInterceptor]),
      ),
      provideHttpClientTesting(),
      { provide: HttpXsrfTokenExtractor, useValue: { getToken: () => token.value } },
    ],
  });
  const navigation = TestBed.inject(BrowserNavigation);
  const assign = vi.spyOn(navigation, 'assign').mockImplementation(() => undefined);
  vi.spyOn(navigation, 'currentPath').mockReturnValue('/admin/pages');
  return {
    client: TestBed.inject(HttpClient),
    http: (controller = TestBed.inject(HttpTestingController)),
    toasts: TestBed.inject(ToastService),
    assign,
    token,
  };
}

describe('CMS HTTP interceptors', () => {
  afterEach(() => {
    controller?.verify();
    controller = undefined;
  });

  describe('XSRF', () => {
    it('puts the readable cookie half in X-XSRF-TOKEN on an unsafe relative request', () => {
      const { client, http } = setup();
      client.post('/api/v1/cms/pages', {}).subscribe();

      expect(http.expectOne('/api/v1/cms/pages').request.headers.get('X-XSRF-TOKEN')).toBe(
        'token-1',
      );
    });

    it('sends no token on a safe request', () => {
      const { client, http } = setup();
      client.get('/api/v1/cms/pages').subscribe();

      expect(http.expectOne('/api/v1/cms/pages').request.headers.has('X-XSRF-TOKEN')).toBe(false);
    });

    it('never sends the token to another origin', () => {
      const { client, http } = setup();
      client.post('https://evil.example/collect', {}).subscribe({ error: () => undefined });

      expect(
        http.expectOne('https://evil.example/collect').request.headers.has('X-XSRF-TOKEN'),
      ).toBe(false);
    });

    it('reads the gateway cookie by its __Host- name', () => {
      expect(XSRF_COOKIE_NAME).toBe('__Host-6311.csrf');
      expect(XSRF_HEADER_NAME).toBe('X-XSRF-TOKEN');
    });
  });

  describe('antiforgery retry', () => {
    it('fetches a fresh pair and sends the write once more with the new token', async () => {
      const { client, http, token } = setup();
      const saved = firstValueFrom(
        client.post<{ ok: boolean }>('/api/v1/cms/pages', { title: 'About' }),
      );

      http
        .expectOne('/api/v1/cms/pages')
        .flush(refusal, { status: 400, statusText: 'Bad Request' });

      token.value = 'token-2';
      const pair = http.expectOne('/bff/antiforgery');
      expect(pair.request.method).toBe('GET');
      pair.flush(null, { status: 204, statusText: 'No Content' });

      const retry = http.expectOne('/api/v1/cms/pages');
      expect(retry.request.headers.get('X-XSRF-TOKEN')).toBe('token-2');
      expect(retry.request.body).toEqual({ title: 'About' });
      retry.flush({ ok: true });

      expect(await saved).toEqual({ ok: true });
    });

    it('retries only once, then reports the refusal', async () => {
      const { client, http, toasts } = setup();
      const saved = firstValueFrom(client.post('/api/v1/cms/pages', {}));

      http
        .expectOne('/api/v1/cms/pages')
        .flush(refusal, { status: 400, statusText: 'Bad Request' });
      http.expectOne('/bff/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
      http
        .expectOne('/api/v1/cms/pages')
        .flush(refusal, { status: 400, statusText: 'Bad Request' });

      await expect(saved).rejects.toMatchObject({ status: 400 });
      http.expectNone('/bff/antiforgery');
      expect(toasts.toasts().map((toast) => toast.heading)).toEqual([refusal.title]);
    });

    it('never retries, and so never sends the token, to another origin', async () => {
      const { client, http } = setup();
      const saved = firstValueFrom(client.post('https://elsewhere.example/api', {}));
      http.expectOne('https://elsewhere.example/api').flush(refusal, { status: 400, statusText: 'Bad Request' });

      await expect(saved).rejects.toMatchObject({ status: 400 });
      http.expectNone('/bff/antiforgery');
    });

    it('leaves any other 400 alone', async () => {
      const { client, http } = setup();
      const saved = firstValueFrom(client.post('/api/v1/cms/pages', {}));

      http
        .expectOne('/api/v1/cms/pages')
        .flush(
          { title: 'Invalid.', code: 'validation.failed' },
          { status: 400, statusText: 'Bad Request' },
        );

      await expect(saved).rejects.toMatchObject({ status: 400 });
      http.expectNone('/bff/antiforgery');
    });
  });

  describe('problems', () => {
    it('toasts a ProblemDetails title with its correlation id', async () => {
      const { client, http, toasts } = setup();
      const loaded = firstValueFrom(client.get('/api/v1/cms/pages'));
      http
        .expectOne('/api/v1/cms/pages')
        .flush(
          {
            title: 'The request could not be completed.',
            code: 'gateway.upstream_failed',
            correlationId: 'abc123',
          },
          { status: 502, statusText: 'Bad Gateway' },
        );

      await expect(loaded).rejects.toMatchObject({ status: 502 });
      expect(toasts.toasts()).toEqual([
        expect.objectContaining({
          tone: 'error',
          heading: 'The request could not be completed.',
          reference: 'abc123',
        }),
      ]);
    });

    it('says something useful when the body is not ProblemDetails', async () => {
      const { client, http, toasts } = setup();
      const loaded = firstValueFrom(client.get('/api/v1/cms/pages'));
      http.expectOne('/api/v1/cms/pages').flush('<html>', { status: 500, statusText: 'Error' });

      await expect(loaded).rejects.toBeTruthy();
      expect(toasts.toasts()[0]).toMatchObject({ heading: 'Something went wrong' });
    });

    it('does not toast what the caller shows itself', async () => {
      const { client, http, toasts } = setup();
      const saved = firstValueFrom(
        client.patch(
          '/api/v1/cms/pages/1',
          {},
          { context: new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true) },
        ),
      );
      http
        .expectOne('/api/v1/cms/pages/1')
        .flush(
          { title: 'Changed elsewhere.', code: 'concurrency.conflict' },
          { status: 409, statusText: 'Conflict' },
        );

      await expect(saved).rejects.toMatchObject({ status: 409 });
      expect(toasts.toasts()).toEqual([]);
    });

    it('sends a 401 to sign-in even when the caller suppressed toasts', async () => {
      const { client, http, assign } = setup();
      const saved = firstValueFrom(
        client.patch('/api/v1/cms/pages/1', {}, { context: new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true) }),
      );
      http
        .expectOne('/api/v1/cms/pages/1')
        .flush({ title: 'Sign in to continue.' }, { status: 401, statusText: 'Unauthorized' });

      await expect(saved).rejects.toMatchObject({ status: 401 });
      expect(assign).toHaveBeenCalledWith('/signin?returnUrl=%2Fadmin%2Fpages');
    });

    it('leaves the session probe’s 401 to the guard', async () => {
      const { client, http, assign } = setup();
      const probe = firstValueFrom(
        client.get('/bff/session', { context: new HttpContext().set(SESSION_PROBE, true).set(SUPPRESS_PROBLEM_TOAST, true) }),
      );
      http.expectOne('/bff/session').flush({ title: 'Sign in to continue.' }, { status: 401, statusText: 'Unauthorized' });

      await expect(probe).rejects.toMatchObject({ status: 401 });
      expect(assign).not.toHaveBeenCalled();
    });

    it('sends a 401 to sign-in, coming back to this screen, without a toast', async () => {
      const { client, http, toasts, assign } = setup();
      const loaded = firstValueFrom(client.get('/api/v1/cms/pages'));
      http
        .expectOne('/api/v1/cms/pages')
        .flush(
          { title: 'Sign in to continue.', code: 'auth.unauthenticated' },
          { status: 401, statusText: 'Unauthorized' },
        );

      await expect(loaded).rejects.toMatchObject({ status: 401 });
      expect(assign).toHaveBeenCalledWith('/signin?returnUrl=%2Fadmin%2Fpages');
      expect(toasts.toasts()).toEqual([]);
    });
  });
});
