import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { captureLinkFragment } from './app/core/link-fragment';

// Before anything else runs: an emailed link's user id and token leave the address bar here, ahead of the router
// and everything else that reads the URL (ADR 0029).
captureLinkFragment(window.location, window.history);

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
