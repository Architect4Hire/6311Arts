import { provideHttpClient, withFetch, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideClientHydration, withEventReplay, withIncrementalHydration } from '@angular/platform-browser';
import { Router, provideRouter, withComponentInputBinding, withInMemoryScrolling, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { isInPage } from './core/navigation';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withViewTransitions({
        onViewTransitionCreated: ({ transition }) => {
          if (isInPage(inject(Router).currentNavigation()?.extras.info)) transition.skipTransition();
        },
      }),
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' }),
    ),
    // The gateway's antiforgery pair (ADR 0024): Angular copies the readable __Host-6311.csrf cookie into the
    // X-XSRF-TOKEN header on unsafe, relative requests — the sign-in post, and the forms from 3.3.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: '__Host-6311.csrf', headerName: 'X-XSRF-TOKEN' }),
    ),
    // Server HTML is reused rather than re-rendered, clicks made before the app wakes up are replayed,
    // and WordPress responses fetched on the server are passed to the browser in the page.
    provideClientHydration(withEventReplay(), withIncrementalHydration()),
  ],
};
