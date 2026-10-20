import { Route, Routes } from '@angular/router';
import { roleGuard } from './core/guards/role.guard';
import { sessionGuard } from './core/guards/session.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';
import { CMS_SECTIONS } from './features/layout/sections';

const artistEditor = () =>
  import('./features/artists/artist-editor').then((m) => m.ArtistEditor);

const exhibitionEditor = () =>
  import('./features/exhibitions/exhibition-editor').then((m) => m.ExhibitionEditor);

const classEditor = () =>
  import('./features/classes/class-editor').then((m) => m.ClassEditor);

const pageEditor = () => import('./features/pages/page-editor').then((m) => m.PageEditor);

const shopItemEditor = () =>
  import('./features/shop/shop-item-editor').then((m) => m.ShopItemEditor);

const redirectEditor = () =>
  import('./features/redirects/redirect-editor').then((m) => m.RedirectEditor);

const placeholder = () =>
  import('./features/section-placeholder/section-placeholder').then((m) => m.SectionPlaceholder);

/** The sections whose screens exist; the rest show the placeholder until their prompt builds them. */
const SCREENS: Readonly<Record<string, Pick<Route, 'loadComponent' | 'canDeactivate'>>> = {
  '': { loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard) },
  exhibitions: {
    loadComponent: () =>
      import('./features/exhibitions/exhibition-list').then((m) => m.ExhibitionList),
  },
  artists: {
    loadComponent: () => import('./features/artists/artist-list').then((m) => m.ArtistList),
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
  redirects: {
    loadComponent: () => import('./features/redirects/redirect-list').then((m) => m.RedirectList),
  },
  media: {
    loadComponent: () => import('./features/media/media-library').then((m) => m.MediaLibrary),
    canDeactivate: [unsavedChangesGuard],
  },
};

/**
 * Screens below a section's own page: an editor for one record. Each keeps its section's roles, and asks before
 * unsaved edits are lost.
 */
const DETAIL_ROUTES: Routes = [
  { path: 'artists/new', title: 'New artist', section: 'artists', loadComponent: artistEditor },
  { path: 'artists/:id', title: 'Artist', section: 'artists', loadComponent: artistEditor },
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
  { path: 'pages/new', title: 'New page', section: 'pages', loadComponent: pageEditor },
  { path: 'pages/:id', title: 'Page', section: 'pages', loadComponent: pageEditor },
  { path: 'redirects/new', title: 'New redirect', section: 'redirects', loadComponent: redirectEditor },
  { path: 'redirects/:id', title: 'Redirect', section: 'redirects', loadComponent: redirectEditor },
].map(({ section, ...route }) => {
  const owner = CMS_SECTIONS.find((candidate) => candidate.path === section)!;
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
      ...CMS_SECTIONS.map((section) => ({
        path: section.path,
        pathMatch: 'full' as const,
        title: section.label,
        canActivate: [roleGuard(...section.roles)],
        data: { section },
        loadComponent: placeholder,
        ...SCREENS[section.path],
      })),
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
