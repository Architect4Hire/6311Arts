import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem, decodeApiProblem } from '../../models/decode';

/** What a refused request said, or `null` when the failure was not an HTTP answer at all. */
export function problemOf(error: unknown): ApiProblem | null {
  return error instanceof HttpErrorResponse ? decodeApiProblem(error.error, error.status) : null;
}

/** How a screen's refusals are put, where it differs from an editor's. */
export interface MessageOptions {
  /** Whether the screen holds edits that could be lost; when it does not, the unreachable message says nothing of them. */
  readonly edits?: boolean;
  /** What a 403 says, where the screen can say it better than the API. */
  readonly forbidden?: string;
}

/**
 * The sentence a screen shows in place for a refusal: the API's own detail, or the screen's fallback. The CMS
 * services suppress the global problem toast, so this is the only place a failure is put into words.
 */
export function messageOf(
  problem: ApiProblem | null,
  fallback: string,
  options: MessageOptions = {},
): string {
  if (problem === null || problem.status === 0) {
    return options.edits === false
      ? 'Can’t reach 6311arts. Try again.'
      : 'Can’t reach 6311arts. Your changes are still here; try again.';
  }
  if (problem.status === 403 && options.forbidden) return options.forbidden;
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
