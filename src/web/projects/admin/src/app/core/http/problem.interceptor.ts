import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { decodeProblem } from '../../models/problem';
import { SessionService } from '../session/session.service';
import { ToastService } from '../toast/toast.service';
import { SESSION_PROBE, SUPPRESS_PROBLEM_TOAST } from './http-context';

/**
 * The CMS's error surface for requests nobody handles themselves:
 *
 * - **401**: the session has ended; go and sign in again, then come back here.
 * - **anything else**: a toast with the ProblemDetails title and its correlation id, unless the caller set
 *   `SUPPRESS_PROBLEM_TOAST` because it shows the error in place (a conflict view, field errors).
 *
 * Known gap (ADR 0027): a 401 navigates away at once, so an editor with unsaved work loses it. The editors that
 * can hold such work arrive in 2.3+; the first of them owes a "your session ended" dialog instead.
 *
 * The error is always rethrown, so the caller still sees its request fail.
 */
export const problemInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const toasts = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);

      if (error.status === 401) {
        // Every 401 means the session ended, whoever made the request — except the probe's, which is the answer
        // "signed out" that the session guard acts on.
        if (!request.context.get(SESSION_PROBE)) session.expired();
      } else if (!request.context.get(SUPPRESS_PROBLEM_TOAST)) {
        toasts.show(describe(error));
      }

      return throwError(() => error);
    }),
  );
};

function describe(error: HttpErrorResponse) {
  if (error.status === 0) {
    return {
      tone: 'error' as const,
      heading: 'Can’t reach 6311arts',
      message: 'Check your connection, then try again.',
    };
  }

  const problem = decodeProblem(error.error, error.status);
  if (problem) {
    return {
      tone: 'error' as const,
      heading: problem.title,
      reference: problem.correlationId ?? undefined,
    };
  }

  return {
    tone: 'error' as const,
    heading: 'Something went wrong',
    message: `The server answered ${error.status}. Try again in a moment.`,
  };
}
