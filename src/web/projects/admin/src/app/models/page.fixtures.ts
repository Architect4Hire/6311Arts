import { Page, PageDeletedRow, PageSummary } from './page';

/** The privacy placeholder as the API returns it, for specs. */
export const PAGE: Page = {
  id: '0199f0a0-0000-7000-8000-0000000000a1',
  title: 'Privacy Policy',
  slug: 'privacy',
  body: '<h2>Placeholder</h2><p>This privacy policy has not been written yet.</p>',
  imageMediaIds: [],
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
  slugLocked: false,
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'system:placeholder-seed',
  etag: '"0123456789abcdef0123456789abcdef"',
};

/** The same page as the Deleted view shows it: what it was, when it went, and the tag a restore sends back. */
export function toPageDeletedRow(
  page: Page,
  deletedAt = '2026-10-21T09:00:00+00:00',
): PageDeletedRow {
  return {
    id: page.id,
    title: page.title,
    slug: page.slug,
    status: page.status,
    deletedAt,
    etag: page.etag,
  };
}

export function toPageSummary(page: Page): PageSummary {
  return {
    id: page.id,
    title: page.title,
    slug: page.slug,
    status: page.status,
    updatedAt: page.updatedAt,
  };
}
