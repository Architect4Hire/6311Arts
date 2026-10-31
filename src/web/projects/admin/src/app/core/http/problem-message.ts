import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem, decodeApiProblem } from '../../models/decode';

/** What a refused request said, or `null` when the failure was not an HTTP answer at all. */
export function problemOf(error: unknown): ApiProblem | null {
  return error instanceof HttpErrorResponse ? decodeApiProblem(error.error, error.status) : null;
}

/**
 * The sentence a screen shows in place for a refusal: the API's own detail, or the screen's fallback. The CMS
 * services suppress the global problem toast, so this is the only place a failure is put into words.
 */
export function messageOf(problem: ApiProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0) {
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  }
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
