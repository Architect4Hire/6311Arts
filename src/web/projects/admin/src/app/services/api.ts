import { HttpContext } from '@angular/common/http';
import { SUPPRESS_PROBLEM_TOAST } from '../core/http/http-context';

/**
 * A request whose refusal the screen shows in place — a field error beside its field, a 409 as a comparison — so the
 * global problem toast stays quiet.
 */
export function quiet(): HttpContext {
  return new HttpContext().set(SUPPRESS_PROBLEM_TOAST, true);
}

/** A decoded body, or an error saying what it should have been. */
export function decoded<T>(value: T | null, what: string): T {
  if (value === null) {
    const article = /^[aeiou]/i.test(what) ? 'an' : 'a';
    throw new Error(`The API returned something that is not ${article} ${what}.`);
  }
  return value;
}
