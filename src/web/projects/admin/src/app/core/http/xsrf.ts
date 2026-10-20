/**
 * The antiforgery pair's browser half (auth.md "CSRF", ADR 0024). The gateway sets `__Host-6311.xsrf` (HttpOnly,
 * the secret) and `__Host-6311.csrf` (readable); Angular's XSRF interceptor copies the readable one into the
 * header on every unsafe, relative request. The token is never in a response body.
 */
export const XSRF_COOKIE_NAME = '__Host-6311.csrf';
export const XSRF_HEADER_NAME = 'X-XSRF-TOKEN';

/** Issues the pair for whoever the session says the browser is. Anonymous and safe. */
export const ANTIFORGERY_PATH = '/bff/antiforgery';
