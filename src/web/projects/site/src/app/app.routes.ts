import { Routes } from '@angular/router';

/** Pages load lazily so each route ships only its own code. Old WordPress URLs are redirected in server.ts. */
export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then((m) => m.Home) },
  {
    path: 'classes',
    loadComponent: () => import('./pages/classes/classes').then((m) => m.Classes),
  },
  {
    path: 'classes/:slug',
    loadComponent: () => import('./pages/class-detail/class-detail').then((m) => m.ClassDetail),
  },
  {
    path: 'exhibitions',
    loadComponent: () => import('./pages/exhibitions/exhibitions').then((m) => m.Exhibitions),
  },
  {
    path: 'instructors',
    loadComponent: () => import('./pages/instructors/instructors').then((m) => m.Instructors),
  },
  {
    path: 'instructors/:slug',
    loadComponent: () =>
      import('./pages/instructor-detail/instructor-detail').then((m) => m.InstructorDetail),
  },
  {
    path: 'artists',
    loadComponent: () => import('./pages/artists/artists').then((m) => m.Artists),
  },
  // Declared before 'artists/:slug' so the form wins over an artist whose slug is "submit".
  {
    path: 'artists/submit',
    loadComponent: () =>
      import('./pages/artist-submission/artist-submission').then((m) => m.ArtistSubmission),
  },
  {
    path: 'artists/:slug',
    loadComponent: () => import('./pages/artist-detail/artist-detail').then((m) => m.ArtistDetail),
  },
  { path: 'shop', loadComponent: () => import('./pages/shop/shop').then((m) => m.Shop) },
  {
    path: 'shop/:slug',
    loadComponent: () =>
      import('./pages/product-detail/product-detail').then((m) => m.ProductDetail),
  },
  {
    path: 'memberships',
    loadComponent: () => import('./pages/memberships/memberships').then((m) => m.Memberships),
  },
  { path: 'about', loadComponent: () => import('./pages/about/about').then((m) => m.About) },
  { path: 'visit', loadComponent: () => import('./pages/visit/visit').then((m) => m.Visit) },
  { path: 'submit', loadComponent: () => import('./pages/submit/submit').then((m) => m.Submit) },
  {
    path: 'privacy',
    loadComponent: () => import('./pages/cms-page/cms-page').then((m) => m.CmsPage),
    data: { pageSlug: 'privacy', fallbackTitle: 'Privacy Policy' },
  },
  {
    path: 'terms',
    loadComponent: () => import('./pages/cms-page/cms-page').then((m) => m.CmsPage),
    data: { pageSlug: 'terms', fallbackTitle: 'Terms and Conditions' },
  },
  // Staff sign-in. Here rather than in the CMS because /admin/** is behind the session gate (ADR 0024).
  { path: 'signin', loadComponent: () => import('./pages/signin/signin').then((m) => m.Signin) },
  // Where the emailed links land (ADR 0028, 0029). main.ts takes their credentials out of the fragment first.
  {
    path: 'account/forgot-password',
    loadComponent: () =>
      import('./pages/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'account/reset-password',
    loadComponent: () =>
      import('./pages/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'account/confirm-email',
    loadComponent: () => import('./pages/confirm-email/confirm-email').then((m) => m.ConfirmEmail),
  },
  // A signed, expiring preview of one unpublished record (ADR 0036, 0063). Never linked to, never in the
  // sitemap, disallowed in robots.txt; the token in the address is the whole credential.
  {
    path: 'preview/:token',
    loadComponent: () => import('./pages/preview/preview').then((m) => m.Preview),
  },
  {
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
