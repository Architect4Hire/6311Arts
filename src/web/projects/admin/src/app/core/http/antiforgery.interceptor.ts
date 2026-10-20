import {
  HttpErrorResponse,
  HttpEventType,
  HttpInterceptorFn,
  HttpRequest,
  HttpXsrfTokenExtractor,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, filter, switchMap, take, throwError } from 'rxjs';
import { PROBLEM_CODES, decodeProblem } from '../../models/problem';
import { ANTIFORGERY_RETRIED } from './http-context';
import { ANTIFORGERY_PATH, XSRF_HEADER_NAME } from './xsrf';

/**
 * A write refused for want of a current antiforgery token (`auth.antiforgery_required`) fetches a fresh pair from
 * `/bff/antiforgery` and is sent once more. The pair can go missing or stale without anyone doing anything wrong:
 * the browser dropped the cookie, or the gateway's key ring moved on.
 *
 * Angular's own XSRF interceptor runs before this one, so the retry sets the header itself from the new cookie.
 */
export const antiforgeryInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(HttpXsrfTokenExtractor);

  return next(request).pipe(
    catchError((error: unknown) => {
      // Relative URLs only, as Angular's own XSRF interceptor does: the retry sets the token itself, and must never
      // hand it to another origin however that origin answered.
      if (
        !isAntiforgeryRefusal(error) ||
        request.context.get(ANTIFORGERY_RETRIED) ||
        !isRelative(request.url)
      ) {
        return throwError(() => error);
      }

      return next(new HttpRequest('GET', ANTIFORGERY_PATH)).pipe(
        // The stream emits a "sent" event before the response; only the response has set the new cookie.
        filter((event) => event.type === HttpEventType.Response),
        take(1),
        switchMap(() => {
          const token = tokens.getToken();
          return next(
            request.clone({
              context: request.context.set(ANTIFORGERY_RETRIED, true),
              ...(token ? { setHeaders: { [XSRF_HEADER_NAME]: token } } : {}),
            }),
          );
        }),
        // A failure to fetch the pair is reported as the original refusal: that is what the caller asked about.
        catchError((retryError: unknown) =>
          throwError(() => (isAntiforgeryRefusal(retryError) ? retryError : error)),
        ),
      );
    }),
  );
};

function isRelative(url: string): boolean {
  return url.startsWith('/') && !url.startsWith('//');
}

function isAntiforgeryRefusal(error: unknown): error is HttpErrorResponse {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 400 &&
    decodeProblem(error.error, error.status)?.code === PROBLEM_CODES.antiforgeryRequired
  );
}
