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
  /**
   * The W3C trace id the API stamps on every `ProblemDetails` (ADR 0019), so a screen that shows a failure in
   * place can give staff the one reference support can follow. The CMS services set `SUPPRESS_PROBLEM_TOAST`,
   * so the global toast that shows this never fires for them — without it here, a failed save left nothing on
   * screen to quote (api-contract.md "Errors").
   */
  readonly correlationId: string | null;
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
    correlationId:
      f && isString(f['correlationId']) && f['correlationId'] !== '' ? f['correlationId'] : null,
  };
}

/** A cursor-paged list (api-contract.md): its rows and the cursor to the next page. */
export interface CursorPage<T> {
  readonly items: readonly T[];
  readonly nextCursor: string | null;
}

/** Decodes a cursor page with each row's own decoder; one row that is not one makes the whole page not one. */
export function decodeCursorPage<T>(
  body: unknown,
  decodeItem: (item: unknown) => T | null,
): CursorPage<T> | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeItem);
  if (items.some((item) => item === null)) return null;
  return { items: items as T[], nextCursor: f['nextCursor'] };
}

/** One field's decoder: its value, or `undefined` when it is not what the field holds. */
export type Field<T> = (value: unknown) => T | undefined;

/** A field decoder from a type guard: the value as it came, when the guard accepts it. */
export const field =
  <T>(guard: (value: unknown) => value is T): Field<T> =>
  (value) =>
    guard(value) ? value : undefined;

export const text = field(isString);
export const optionalText = field(isNullableString);
export const flag = field(isBoolean);
export const amount = field(isNumber);
export const optionalAmount = field(isNullableNumber);

/** An int64 the API may send as a number or a numeric string; `null` stays `null`. */
export function int64(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

/** A shape's decoded value: each field as its decoder gives it. */
export type Shaped<S> = { readonly [K in keyof S]: S[K] extends Field<infer T> ? T : never };

/**
 * Decodes an object field by field: each field of `shape` names its decoder, and one field that is not what it
 * should be makes the whole body not one. Only the shape's fields are kept.
 */
export function decodeShape<S extends Record<string, Field<unknown>>>(
  body: unknown,
  shape: S,
): Shaped<S> | null {
  const f = fields(body);
  if (!f) return null;
  const decoded: Record<string, unknown> = {};
  for (const key of Object.keys(shape)) {
    const value = shape[key](f[key]);
    if (value === undefined) return null;
    decoded[key] = value;
  }
  return decoded as Shaped<S>;
}

/** A field that may also be `null`. */
export const nullable =
  <T>(decode: Field<T>): Field<T | null> =>
  (value) =>
    value === null ? null : decode(value);

/** A field holding an object of its own, decoded by its shape. */
export const shaped =
  <S extends Record<string, Field<unknown>>>(shape: S): Field<Shaped<S>> =>
  (value) =>
    decodeShape(value, shape) ?? undefined;

/** A field holding a list, each item decoded; one item that is not one makes the field not one. */
export const listOf =
  <T>(decode: Field<T>): Field<readonly T[]> =>
  (value) => {
    if (!Array.isArray(value)) return undefined;
    const items: T[] = [];
    for (const item of value) {
      const decoded = decode(item);
      if (decoded === undefined) return undefined;
      items.push(decoded);
    }
    return items;
  };

/** A whole number that must be there: an int32 or int64 the API may send as a number or a numeric string. */
export function whole(value: unknown): number | undefined {
  const number = int64(value);
  return number === null ? undefined : number;
}
