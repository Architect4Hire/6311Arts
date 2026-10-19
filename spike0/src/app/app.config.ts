import { provideHttpClient, withFetch } from '@angular/common/http';
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
    provideHttpClient(withFetch()),
    // Server HTML is reused rather than re-rendered, clicks made before the app wakes up are replayed,
    // and WordPress responses fetched on the server are passed to the browser in the page.
    provideClientHydration(withEventReplay(), withIncrementalHydration()),
  ],
};
