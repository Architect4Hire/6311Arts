/**
 * What `/bff/session` returns, and the whole of it: a display name and roles, never an id, an email address or a
 * token (auth.md). Mirrors the gateway's `SessionResponse`.
 */
export interface Session {
  readonly displayName: string;
  readonly roles: readonly string[];
}

/** The role names as the gateway and the API spell them (`GatewayRoles`, `IdentityRoles`). */
export const ROLES = {
  admin: 'Admin',
  editor: 'Editor',
  member: 'Member',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

/** The roles that reach `/admin` at all: the gateway's `CmsEditor` policy. */
export const STAFF_ROLES: readonly Role[] = [ROLES.admin, ROLES.editor];

/** Decodes an unknown body into a `Session`, or `null` if it is not one. Bodies are untrusted at the boundary. */
export function decodeSession(body: unknown): Session | null {
  if (typeof body !== 'object' || body === null) return null;
  const { displayName, roles } = body as Record<string, unknown>;
  if (typeof displayName !== 'string' || !Array.isArray(roles)) return null;
  if (!roles.every((role): role is string => typeof role === 'string')) return null;
  return { displayName, roles };
}
