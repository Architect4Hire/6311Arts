/**
 * Where a sign-in goes when nothing (or nothing safe) asked for anywhere else: the home page.
 *
 * The gateway always writes `returnUrl` from the gated path it turned away (ADR 0024), so someone sent here from
 * `/admin/artists` still lands back on `/admin/artists`. This is the other arrival — the footer's "Staff sign-in",
 * where no particular screen was asked for — and the site's own front door is the honest answer for it. The CMS is
 * then one click away in the footer (`StaffAccess`), in a new tab.
 */
export const DEFAULT_RETURN_URL = '/';

/** The CMS's base href. It is another app on this origin, so every link to it is a full navigation. */
export const CMS_HOME = '/admin/';

/** A throwaway origin to resolve against: anything that resolves elsewhere was trying to leave this site. */
const PROBE_ORIGIN = 'https://signin.invalid';

/** Paths a sign-in must never send someone back to: itself, the session endpoints and the raw API. */
const REFUSED_PREFIXES = ['/signin', '/bff', '/api'];

/**
 * `?returnUrl` comes from the address bar, so it is attacker-controlled: an open redirect here would put a real
 * 6311arts sign-in page in front of a phishing site. Only a path on this origin survives; anything else becomes
 * {@link DEFAULT_RETURN_URL}.
 *
 * The gateway writes `returnUrl` from the request's own path (ADR 0024), so a legitimate value always passes.
 */
export function safeReturnUrl(raw: unknown): string {
  // `unknown`, not `string`: a repeated `?returnUrl=…&returnUrl=…` arrives through input binding as an array.
  if (typeof raw !== 'string' || !raw || raw.length > 2048) return DEFAULT_RETURN_URL;

  // One leading slash, and no backslash or control character anywhere: browsers read "\" as "/" and strip tabs
  // and newlines, which turns "/\evil.example" or "/\t/evil.example" into "//evil.example".
  if (!raw.startsWith('/') || raw.startsWith('//')) return DEFAULT_RETURN_URL;
  if (/[\\\u0000-\u001f\u007f]/.test(raw)) return DEFAULT_RETURN_URL;

  let url: URL;
  try {
    url = new URL(raw, PROBE_ORIGIN);
  } catch {
    return DEFAULT_RETURN_URL;
  }
  if (url.origin !== PROBE_ORIGIN) return DEFAULT_RETURN_URL;

  // Dot segments collapse during parsing: "/admin/../..//evil.example" resolves to the path "//evil.example",
  // which a browser then reads as another origin. Check the normalised path, not only the raw one.
  if (url.pathname.startsWith('//')) return DEFAULT_RETURN_URL;

  // Compared decoded, so "/%62ff/logout" is still "/bff/logout". A path that will not decode is refused outright.
  let path: string;
  try {
    path = decodeURIComponent(url.pathname).toLowerCase();
  } catch {
    return DEFAULT_RETURN_URL;
  }
  if (REFUSED_PREFIXES.some((prefix) => path === prefix || path.startsWith(prefix + '/'))) {
    return DEFAULT_RETURN_URL;
  }

  // The CMS is served under /admin/ (its base href); the bare prefix is the same place.
  const pathname = url.pathname === '/admin' ? CMS_HOME : url.pathname;
  return pathname + url.search + url.hash;
}

/** Whether the destination is the CMS, which needs a staff role as well as a session. */
export function isCmsPath(path: string): boolean {
  const lower = path.toLowerCase();
  return lower === '/admin' || lower.startsWith('/admin/');
}
