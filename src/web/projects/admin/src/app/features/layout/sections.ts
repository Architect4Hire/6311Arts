import { IconName } from '@6311/ui';
import { ROLES, Role, STAFF_ROLES } from '../../models/session';

/** One area of the CMS: a nav entry, a route and the roles that may use it. */
export interface CmsSection {
  /** Route path under `/admin/`. Empty for the dashboard. */
  readonly path: string;
  readonly label: string;
  readonly icon: IconName;
  readonly roles: readonly Role[];
  /** What the section is for, shown until its screens exist. */
  readonly summary: string;
}

/**
 * The CMS's areas, in nav order. Each later prompt replaces a section's placeholder with its own screens; the
 * entry here stays. Sync is Admin-only because it is where poisoned outbox messages are replayed or discarded, and
 * Redirects because every redirect change is a sensitive operation (auth.md, "Sensitive operations").
 */
export const CMS_SECTIONS: readonly CmsSection[] = [
  {
    path: '',
    label: 'Dashboard',
    icon: 'grid',
    roles: STAFF_ROLES,
    summary: 'What needs attention across the site.',
  },
  {
    path: 'classes',
    label: 'Classes',
    icon: 'calendar',
    roles: STAFF_ROLES,
    summary:
      'Class and workshop pages and their runs. Seats and prices are managed in Square.',
  },
  {
    path: 'exhibitions',
    label: 'Exhibitions',
    icon: 'frame',
    roles: STAFF_ROLES,
    summary: 'Current and upcoming shows, with their dates, artists and images.',
  },
  {
    path: 'artists',
    label: 'Artists',
    icon: 'brush',
    roles: STAFF_ROLES,
    summary: 'The artist roster: names, bios, mediums and portraits.',
  },
  {
    path: 'instructors',
    label: 'Instructors',
    icon: 'people',
    roles: STAFF_ROLES,
    summary: 'Who teaches the classes: names, bios, portraits and links.',
  },
  {
    path: 'shop',
    label: 'Shop',
    icon: 'cart',
    roles: STAFF_ROLES,
    summary: 'Shop item copy and images. Price and stock are managed in Square.',
  },
  {
    path: 'memberships',
    label: 'Memberships',
    icon: 'heart',
    roles: STAFF_ROLES,
    summary: 'Membership plan copy. Billing and member status are managed in Square.',
  },
  {
    path: 'pages',
    label: 'Pages',
    icon: 'info',
    roles: STAFF_ROLES,
    summary: 'Standalone pages such as About, Visit and the policies.',
  },
  {
    path: 'media',
    label: 'Media',
    icon: 'star',
    roles: STAFF_ROLES,
    summary: 'The image library, alt text and renditions.',
  },
  {
    path: 'forms',
    label: 'Forms',
    icon: 'mail',
    roles: STAFF_ROLES,
    summary: 'Submissions from the contact, newsletter and artist forms.',
  },
  {
    path: 'redirects',
    label: 'Redirects',
    icon: 'arrow',
    // Every redirect change is a sensitive operation (auth.md), Admin-only at the API too (ADR 0035).
    roles: [ROLES.admin],
    summary: 'Old addresses and where they now lead.',
  },
  {
    path: 'sync',
    label: 'Sync',
    icon: 'infinity',
    roles: [ROLES.admin],
    summary: 'Square sync status, and messages that need a replay or a decision.',
  },
];
