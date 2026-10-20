/**
 * The `ProblemDetails` envelope the gateway and the API both write (`GatewayProblem`, `ApiProblemDetails`):
 * RFC 9457 fields plus a stable `code` and the W3C trace id as `correlationId` (ADR 0019).
 */
export interface Problem {
  readonly status: number;
  readonly title: string;
  readonly code: string | null;
  readonly correlationId: string | null;
}

/** The codes the CMS acts on rather than only shows. */
export const PROBLEM_CODES = {
  antiforgeryRequired: 'auth.antiforgery_required',
  unauthenticated: 'auth.unauthenticated',
} as const;

/** Decodes an error body into a `Problem`, or `null` when it is not ProblemDetails (a proxy's HTML page, say). */
export function decodeProblem(body: unknown, status: number): Problem | null {
  if (typeof body !== 'object' || body === null) return null;
  const fields = body as Record<string, unknown>;
  if (typeof fields['title'] !== 'string') return null;
  return {
    status: typeof fields['status'] === 'number' ? fields['status'] : status,
    title: fields['title'],
    code: typeof fields['code'] === 'string' ? fields['code'] : null,
    correlationId:
      typeof fields['correlationId'] === 'string' && fields['correlationId'] !== ''
        ? fields['correlationId']
        : null,
  };
}
