import { Routes } from '@angular/router';

/** Pages load lazily so each route ships only its own code. Old WordPress URLs are redirected in server.ts. */
export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home').then((m) => m.Home) },
  { path: 'classes', loadComponent: () => import('./pages/classes').then((m) => m.Classes) },
  { path: 'classes/:slug', loadComponent: () => import('./pages/class-detail').then((m) => m.ClassDetail) },
  { path: 'exhibitions', loadComponent: () => import('./pages/exhibitions').then((m) => m.Exhibitions) },
  { path: 'instructors', loadComponent: () => import('./pages/instructors').then((m) => m.Instructors) },
  {
    path: 'instructors/:slug',
    loadComponent: () => import('./pages/instructors').then((m) => m.InstructorDetail),
  },
  { path: 'artists', loadComponent: () => import('./pages/artists').then((m) => m.Artists) },
  // Declared before 'artists/:slug' so the form wins over an artist whose slug is "submit".
  {
    path: 'artists/submit',
    loadComponent: () => import('./pages/artist-submission').then((m) => m.ArtistSubmission),
  },
  { path: 'artists/:slug', loadComponent: () => import('./pages/artists').then((m) => m.ArtistDetail) },
  { path: 'shop', loadComponent: () => import('./pages/shop').then((m) => m.Shop) },
  { path: 'shop/:slug', loadComponent: () => import('./pages/shop').then((m) => m.ProductDetail) },
  { path: 'memberships', loadComponent: () => import('./pages/info-pages').then((m) => m.Memberships) },
  { path: 'about', loadComponent: () => import('./pages/info-pages').then((m) => m.About) },
  { path: 'visit', loadComponent: () => import('./pages/info-pages').then((m) => m.Visit) },
  { path: 'submit', loadComponent: () => import('./pages/info-pages').then((m) => m.Submit) },
  {
    path: 'privacy',
    loadComponent: () => import('./pages/info-pages').then((m) => m.CmsPage),
    data: { wpSlug: 'privacy-policy', title: 'Privacy Policy' },
  },
  {
    path: 'terms',
    loadComponent: () => import('./pages/info-pages').then((m) => m.CmsPage),
    data: { wpSlug: 'terms-and-conditions', title: 'Terms and Conditions' },
  },
  // Staff sign-in. Here rather than in the CMS because /admin/** is behind the session gate (ADR 0024).
  { path: 'signin', loadComponent: () => import('./pages/signin').then((m) => m.Signin) },
  // Where the emailed links land (ADR 0028, 0029). main.ts takes their credentials out of the fragment first.
  {
    path: 'account/forgot-password',
    loadComponent: () => import('./pages/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'account/reset-password',
    loadComponent: () => import('./pages/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'account/confirm-email',
    loadComponent: () => import('./pages/confirm-email').then((m) => m.ConfirmEmail),
  },
  { path: '**', loadComponent: () => import('./pages/not-found').then((m) => m.NotFound) },
];
