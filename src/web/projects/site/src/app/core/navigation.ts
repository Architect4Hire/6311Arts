/**
 * Passed as a navigation's `info` when only the page's query params change (a filter, a view switch),
 * so the router skips the page cross-fade. Pair it with `scroll: 'manual'` to keep the scroll position.
 */
export const IN_PAGE = { inPage: true } as const;

export function isInPage(info: unknown): boolean {
  return (info as typeof IN_PAGE | undefined)?.inPage === true;
}
