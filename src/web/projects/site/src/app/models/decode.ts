/**
 * Boundary helpers for decoding API bodies, which are untrusted: every model has a decoder that returns `null`
 * rather than trusting a cast (frontend.md, "Decode unknown data at boundaries"). The site's own copy of the CMS's
 * helpers: the two apps share no code but `@6311/ui`.
 */

export type Fields = Record<string, unknown>;

export const isString = (value: unknown): value is string => typeof value === 'string';
/** Every number the site read API sends is an integer: cents, counts, pixels, percentages. */
export const isNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value);
export const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean';
export const isNullableString = (value: unknown): value is string | null =>
  value === null || isString(value);
export const isNullableNumber = (value: unknown): value is number | null =>
  value === null || isNumber(value);

/** The body as an object of fields, or `null` when it is not a plain object. */
export function fields(body: unknown): Fields | null {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
    ? (body as Fields)
    : null;
}

/** Every element decoded, or `null` when the value is not an array or any element fails. */
export function decodeArray<T>(value: unknown, decode: (item: unknown) => T | null): T[] | null {
  if (!Array.isArray(value)) return null;
  const out: T[] = [];
  for (const item of value) {
    const decoded = decode(item);
    if (decoded === null) return null;
    out.push(decoded);
  }
  return out;
}

/** A nullable nested object: `null` stays `null`, anything else must decode. `undefined` means it failed. */
export function decodeNullable<T>(
  value: unknown,
  decode: (item: unknown) => T | null,
): T | null | undefined {
  if (value === null) return null;
  const decoded = decode(value);
  return decoded === null ? undefined : decoded;
}
