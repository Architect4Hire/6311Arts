import { Routes } from '@angular/router';
import { roleGuard } from './core/guards/role.guard';
import { sessionGuard } from './core/guards/session.guard';
import { CMS_SECTIONS } from './features/layout/sections';

const placeholder = () =>
  import('./features/section-placeholder/section-placeholder').then((m) => m.SectionPlaceholder);

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
        loadComponent:
          section.path === ''
            ? () => import('./features/dashboard/dashboard').then((m) => m.Dashboard)
            : placeholder,
      })),
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
