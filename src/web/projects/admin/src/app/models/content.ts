/**
 * What every CMS content type shares (content.md, ADR 0031): the publishing lifecycle, how a status reads, the
 * problem codes the editors act on, and the slug rules. Each type's own model re-uses these rather than its own.
 */

import { IconName, StatusTone } from '@6311/ui';

import { fields, isString } from './decode';

/** `PublishStatus`: the content lifecycle every CMS type shares. */
export const PUBLISH_STATUSES = ['Draft', 'Scheduled', 'Published', 'Archived'] as const;
export type PublishStatus = (typeof PUBLISH_STATUSES)[number];

/** How each status reads in a chip: a word, a glyph and a tone, never colour alone (design-system.md). */
export const STATUS_CHIPS: Readonly<
  Record<
    PublishStatus,
    { readonly label: string; readonly icon: IconName; readonly tone: StatusTone }
  >
> = {
  Draft: { label: 'Draft', icon: 'info', tone: 'neutral' },
  Scheduled: { label: 'Scheduled', icon: 'clock', tone: 'pending' },
  Published: { label: 'Published', icon: 'check', tone: 'positive' },
  Archived: { label: 'Archived', icon: 'close', tone: 'neutral' },
};

/** The problem codes the content editors act on (`ContentErrorCodes`, `ErrorCodes`). */
export const CONTENT_CODES = {
  slugTaken: 'cms.slug_taken',
  /** No longer returned since 2.7: a published slug may change, and the old address is redirected (ADR 0035). */
  slugLocked: 'cms.slug_locked',
  transitionInvalid: 'cms.status_transition_invalid',
  concurrencyConflict: 'concurrency.conflict',
  notFound: 'resource.not_found',
  validationFailed: 'validation.failed',
} as const;

/** The status commands, as their route segments. */
export type ContentCommand = 'publish' | 'unpublish' | 'archive' | 'schedule' | 'unschedule';

/** The body of the `schedule` command (`ContentScheduleViewModel`): when to publish, as an instant. */
export interface ContentSchedule {
  readonly scheduledFor: string;
}

/** The content types a preview link can be made for (`PreviewPolicy.EntityTypes`). */
export const PREVIEW_TYPES = [
  'artist',
  'instructor',
  'exhibition',
  'page',
  'class-offering',
  'shop-item',
  'membership-plan',
] as const;
export type PreviewType = (typeof PREVIEW_TYPES)[number];

/** A preview link (`PreviewLinkServiceModel`): the token is the whole credential, so it is never stored. */
export interface PreviewLink {
  readonly token: string;
  /** Where to open it on the site, `/preview/{token}`. */
  readonly path: string;
  readonly expiresAt: string;
}

export function decodePreviewLink(value: unknown): PreviewLink | null {
  const body = fields(value);
  if (!body) return null;

  const { token, path, expiresAt } = body;
  if (!isString(token) || !isString(path) || !isString(expiresAt)) return null;
  return { token, path, expiresAt };
}

/** `Slugs.MaxLength`. */
export const SLUG_MAX_LENGTH = 120;

/**
 * The slug the API makes from a name or title when none is sent (`Slugs.FromName`): accents dropped, every other run
 * of non-alphanumerics one hyphen, none at either end. Shown as the form's suggestion; the API has the last word.
 */
export function slugFromName(name: string): string {
  const slug = name
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug.length <= SLUG_MAX_LENGTH ? slug : slug.slice(0, SLUG_MAX_LENGTH).replace(/-+$/, '');
}

/** The shape the API accepts as a slug (`Slugs.IsValid`). */
export function isSlug(value: string): boolean {
  return (
    value.length > 0 && value.length <= SLUG_MAX_LENGTH && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)
  );
}
