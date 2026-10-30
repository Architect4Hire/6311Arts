import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import {
  ApplicationConfig,
  Injectable,
  inject,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import {
  RouterStateSnapshot,
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
} from '@angular/router';
import { routes } from './app.routes';
import { antiforgeryInterceptor } from './core/http/antiforgery.interceptor';
import { problemInterceptor } from './core/http/problem.interceptor';
import { XSRF_COOKIE_NAME, XSRF_HEADER_NAME } from './core/http/xsrf';

/** "Classes · 6311arts Studio", so a tab or a history entry says which screen it was. */
@Injectable()
class CmsTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · 6311arts Studio` : '6311arts Studio');
  }
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: CmsTitleStrategy },
    // The default XHR backend, not withFetch(): fetch cannot report upload progress, and the media uploader shows it
    // (ADR 0030). The CMS is client-rendered only, so fetch's SSR advantages never applied here.
    provideHttpClient(
      // Angular's own XSRF interceptor, pointed at the gateway's pair (ADR 0024). It adds the header to unsafe,
      // relative requests only, so the token never travels to another origin.
      withXsrfConfiguration({ cookieName: XSRF_COOKIE_NAME, headerName: XSRF_HEADER_NAME }),
      // Outermost first: the problem interceptor sees a request's final outcome, after any antiforgery retry.
      withInterceptors([problemInterceptor, antiforgeryInterceptor]),
    ),
  ],
};
