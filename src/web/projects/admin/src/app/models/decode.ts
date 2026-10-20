/**
 * Boundary helpers for decoding API bodies, which are untrusted: every model has a decoder that returns `null`
 * rather than trusting a cast (frontend.md, "Decode unknown data at boundaries").
 */

export type Fields = Record<string, unknown>;

export const isString = (value: unknown): value is string => typeof value === 'string';
export const isNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);
export const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean';
export const isNullableString = (value: unknown): value is string | null =>
  value === null || isString(value);
export const isNullableNumber = (value: unknown): value is number | null =>
  value === null || isNumber(value);
export const oneOf =
  <T extends string>(values: readonly T[]) =>
  (value: unknown): value is T =>
    isString(value) && (values as readonly string[]).includes(value);

/** The body as an object of fields, or `null` when it is not a plain object. */
export function fields(body: unknown): Fields | null {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
    ? (body as Fields)
    : null;
}

/** What a refused API request said: its stable code, its message and any per-field errors. */
export interface ApiProblem {
  readonly status: number;
  readonly code: string | null;
  readonly detail: string | null;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
}

/** Decodes an error body; a body that is not ProblemDetails (a proxy page, a dropped connection) gives a bare status. */
export function decodeApiProblem(body: unknown, status: number): ApiProblem {
  const f = fields(body);
  const errors = f ? fields(f['errors']) : null;
  const fieldErrors: Record<string, readonly string[]> = {};
  for (const [key, value] of Object.entries(errors ?? {})) {
    if (Array.isArray(value) && value.every(isString)) fieldErrors[key] = value;
  }

  return {
    status,
    code: f && isString(f['code']) ? f['code'] : null,
    detail: f && isString(f['detail']) ? f['detail'] : null,
    fieldErrors,
  };
}
