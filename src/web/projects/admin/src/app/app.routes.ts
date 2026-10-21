import { Route, Routes } from '@angular/router';
import { roleGuard } from './core/guards/role.guard';
import { sessionGuard } from './core/guards/session.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';
import { CMS_SECTIONS, CmsSection } from './features/layout/sections';

const artistEditor = () =>
  import('./features/artists/artist-editor').then((m) => m.ArtistEditor);

const instructorEditor = () =>
  import('./features/instructors/instructor-editor').then((m) => m.InstructorEditor);

const exhibitionEditor = () =>
  import('./features/exhibitions/exhibition-editor').then((m) => m.ExhibitionEditor);

const classEditor = () =>
  import('./features/classes/class-editor').then((m) => m.ClassEditor);

const pageEditor = () => import('./features/pages/page-editor').then((m) => m.PageEditor);

const shopItemEditor = () =>
  import('./features/shop/shop-item-editor').then((m) => m.ShopItemEditor);

const membershipPlanEditor = () =>
  import('./features/memberships/membership-plan-editor').then((m) => m.MembershipPlanEditor);

const redirectEditor = () =>
  import('./features/redirects/redirect-editor').then((m) => m.RedirectEditor);

const submissionDetail = () =>
  import('./features/forms/submission-detail').then((m) => m.SubmissionDetail);

const messageDetail = () =>
  import('./features/forms/message-detail').then((m) => m.MessageDetail);

const placeholder = () =>
  import('./features/section-placeholder/section-placeholder').then((m) => m.SectionPlaceholder);

/**
 * The sections whose screens exist; the rest show the placeholder until their prompt builds them. A section whose
 * screens are tabs (`SECTION_TABS`) redirects to its first one instead.
 */
const SCREENS: Readonly<
  Record<string, Pick<Route, 'loadComponent' | 'canDeactivate'> | { readonly redirectTo: string }>
> = {
  '': { loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard) },
  exhibitions: {
    loadComponent: () =>
      import('./features/exhibitions/exhibition-list').then((m) => m.ExhibitionList),
  },
  artists: {
    loadComponent: () => import('./features/artists/artist-list').then((m) => m.ArtistList),
  },
  instructors: {
    loadComponent: () =>
      import('./features/instructors/instructor-list').then((m) => m.InstructorList),
  },
  classes: {
    loadComponent: () => import('./features/classes/class-list').then((m) => m.ClassList),
  },
  pages: {
    loadComponent: () => import('./features/pages/page-list').then((m) => m.PageList),
  },
  shop: {
    loadComponent: () => import('./features/shop/shop-list').then((m) => m.ShopList),
  },
  memberships: {
    loadComponent: () =>
      import('./features/memberships/membership-list').then((m) => m.MembershipList),
  },
  redirects: {
    loadComponent: () => import('./features/redirects/redirect-list').then((m) => m.RedirectList),
  },
  media: {
    loadComponent: () => import('./features/media/media-library').then((m) => m.MediaLibrary),
    canDeactivate: [unsavedChangesGuard],
  },
  forms: { redirectTo: '/forms/submissions' },
};

/** The section that owns a route below it, whose roles and nav entry it shares. */
function ownedBy(section: string): CmsSection {
  return CMS_SECTIONS.find((candidate) => candidate.path === section)!;
}

/**
 * A section's own screens when it has more than one: tabs, each a list at its own address (ADR 0047's Forms
 * inbox). They keep the section's roles; none is an editor, so none asks about unsaved changes.
 */
const SECTION_TABS: Routes = [
  {
    path: 'forms/submissions',
    title: 'Submissions',
    section: 'forms',
    loadComponent: () =>
      import('./features/forms/submission-list').then((m) => m.SubmissionList),
  },
  {
    path: 'forms/messages',
    title: 'Messages',
    section: 'forms',
    loadComponent: () => import('./features/forms/message-list').then((m) => m.MessageList),
  },
  {
    path: 'forms/newsletter',
    title: 'Newsletter',
    section: 'forms',
    loadComponent: () =>
      import('./features/forms/newsletter-list').then((m) => m.NewsletterList),
  },
].map(({ section, ...route }) => {
  const owner = ownedBy(section);
  return { ...route, canActivate: [roleGuard(...owner.roles)], data: { section: owner } };
});

/**
 * Screens below a section's own page: an editor for one record. Each keeps its section's roles, and asks before
 * unsaved edits are lost.
 */
const DETAIL_ROUTES: Routes = [
  { path: 'artists/new', title: 'New artist', section: 'artists', loadComponent: artistEditor },
  { path: 'artists/:id', title: 'Artist', section: 'artists', loadComponent: artistEditor },
  {
    path: 'instructors/new',
    title: 'New instructor',
    section: 'instructors',
    loadComponent: instructorEditor,
  },
  {
    path: 'instructors/:id',
    title: 'Instructor',
    section: 'instructors',
    loadComponent: instructorEditor,
  },
  {
    path: 'exhibitions/new',
    title: 'New exhibition',
    section: 'exhibitions',
    loadComponent: exhibitionEditor,
  },
  {
    path: 'exhibitions/:id',
    title: 'Exhibition',
    section: 'exhibitions',
    loadComponent: exhibitionEditor,
  },
  { path: 'classes/new', title: 'New class', section: 'classes', loadComponent: classEditor },
  { path: 'classes/:id', title: 'Class', section: 'classes', loadComponent: classEditor },
  { path: 'shop/new', title: 'New piece', section: 'shop', loadComponent: shopItemEditor },
  { path: 'shop/:id', title: 'Piece', section: 'shop', loadComponent: shopItemEditor },
  {
    path: 'memberships/new',
    title: 'New tier',
    section: 'memberships',
    loadComponent: membershipPlanEditor,
  },
  {
    path: 'memberships/:id',
    title: 'Membership tier',
    section: 'memberships',
    loadComponent: membershipPlanEditor,
  },
  { path: 'pages/new', title: 'New page', section: 'pages', loadComponent: pageEditor },
  { path: 'pages/:id', title: 'Page', section: 'pages', loadComponent: pageEditor },
  { path: 'redirects/new', title: 'New redirect', section: 'redirects', loadComponent: redirectEditor },
  { path: 'redirects/:id', title: 'Redirect', section: 'redirects', loadComponent: redirectEditor },
  {
    path: 'forms/submissions/:id',
    title: 'Submission',
    section: 'forms',
    loadComponent: submissionDetail,
  },
  { path: 'forms/messages/:id', title: 'Message', section: 'forms', loadComponent: messageDetail },
].map(({ section, ...route }) => {
  const owner = ownedBy(section);
  return {
    ...route,
    canActivate: [roleGuard(...owner.roles)],
    canDeactivate: [unsavedChangesGuard],
    data: { section: owner },
  };
});

/**
 * Everything under the shell needs a staff session (`sessionGuard`); a section limited to some roles adds
 * `roleGuard`. The gateway enforces the same gate on `/admin/**` before any of this loads (ADR 0024) — these
 * guards meet the sessions that end while the SPA is open.
 */
export const routes: Routes = [
  {
    path: 'unavailable',
    title: 'Unavailable',
    loadComponent: () => import('./features/unavailable/unavailable').then((m) => m.Unavailable),
  },
  {
    path: '',
    loadComponent: () => import('./features/layout/shell').then((m) => m.Shell),
    canActivate: [sessionGuard],
    children: [
      ...CMS_SECTIONS.map((section): Route => {
        const screen = SCREENS[section.path];
        // A redirect runs before any guard, so it carries none: the screen it leads to has the section's.
        if (screen && 'redirectTo' in screen) {
          return { path: section.path, pathMatch: 'full', redirectTo: screen.redirectTo };
        }
        return {
          path: section.path,
          pathMatch: 'full',
          title: section.label,
          canActivate: [roleGuard(...section.roles)],
          data: { section },
          loadComponent: placeholder,
          ...screen,
        };
      }),
      ...SECTION_TABS,
      ...DETAIL_ROUTES,
      {
        path: 'forbidden',
        title: 'Not available',
        loadComponent: () => import('./features/forbidden/forbidden').then((m) => m.Forbidden),
      },
      {
        path: '**',
        title: 'Not found',
        loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
