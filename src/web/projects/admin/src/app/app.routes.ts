import { Route, Routes } from '@angular/router';
import { roleGuard } from './core/guards/role.guard';
import { sessionGuard } from './core/guards/session.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';
import { CMS_SECTIONS } from './features/layout/sections';

const artistEditor = () =>
  import('./features/artists/artist-editor').then((m) => m.ArtistEditor);

const placeholder = () =>
  import('./features/section-placeholder/section-placeholder').then((m) => m.SectionPlaceholder);

/** The sections whose screens exist; the rest show the placeholder until their prompt builds them. */
const SCREENS: Readonly<Record<string, Pick<Route, 'loadComponent' | 'canDeactivate'>>> = {
  '': { loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard) },
  artists: {
    loadComponent: () => import('./features/artists/artist-list').then((m) => m.ArtistList),
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
