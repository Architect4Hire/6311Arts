import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { UiButton, UiControl, UiField, UiNotice } from '@6311/ui';
import { BffSessionService, Session, isStaff } from '../core/bff-session.service';
import { BrowserLocation } from '../core/browser-location';
import { noStore } from '../core/no-store';
import { isCmsPath, safeReturnUrl } from '../core/return-url';
import { Seo } from '../core/seo.service';
import { SigninNotice, arrivedWith } from '../core/signin-notice';
import { AccountPanel } from '../shared/account-panel';

type Status =
  | { readonly kind: 'idle' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'refused' }
  | { readonly kind: 'rate-limited'; readonly minutes: number | null }
  | { readonly kind: 'unavailable' }
  | { readonly kind: 'not-staff'; readonly name: string };

/**
 * The staff sign-in page (ADR 0024, 0027). It lives on the public site because `/admin/**` is behind the session
 * gate: a login screen inside the CMS bundle could not load its own scripts and would redirect to itself forever.
 *
 * One step, email and password, no second factor (ADR 0018). It posts to the gateway's `/bff/login`, and on success
 * leaves for `?returnUrl` — only ever a path on this origin (`safeReturnUrl`).
 *
 * Rendered on the server like every page, but the fields stay disabled until the app is running in the browser:
 * submitted natively before then, a form would put the password in a URL. There is deliberately no <noscript>
 * note: with scripting on, a browser parses its content as text, and hydration then fails on the mismatch (NG0500)
 * and leaves the form disabled — found walking the page through the gateway.
 */
@Component({
  selector: 'app-signin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, UiButton, UiControl, UiField, UiNotice, AccountPanel],
  template: `
    <app-account-panel
      heading="Staff sign-in"
      lead="For 6311arts staff and volunteers who edit the site."
    >
        <!-- In the page from the first render, so what appears in it later is announced (see ui-notice). -->
        <div class="account-panel__status" aria-live="polite">
          @switch (notice()) {
            @case ('password-changed') {
              <ui-notice tone="success" heading="Your password has been changed">
                <p>Sign in with your new password.</p>
              </ui-notice>
            }
            @case ('email-confirmed') {
              <ui-notice tone="success" heading="Your email address is confirmed">
                <p>You can sign in now.</p>
              </ui-notice>
            }
          }
          @if (signedInAs(); as who) {
            <ui-notice tone="info" heading="You’re already signed in">
              <p>
                You’re signed in as {{ who.displayName }}. <a [href]="destination()">Continue</a>, or
                sign in below as someone else.
              </p>
            </ui-notice>
          }
          @switch (status().kind) {
            @case ('refused') {
              <ui-notice tone="error" heading="Sign-in didn’t work">
                <p>Check your email address and password, then try again.</p>
              </ui-notice>
            }
            @case ('rate-limited') {
              <ui-notice tone="error" heading="Too many attempts">
                <p>{{ rateLimitedMessage() }}</p>
              </ui-notice>
            }
            @case ('unavailable') {
              <ui-notice tone="error" heading="Sign-in is unavailable">
                <p>Something on our side isn’t answering. Try again shortly.</p>
              </ui-notice>
            }
            @case ('not-staff') {
              <ui-notice tone="warning" heading="This account can’t use the CMS">
                <p>
                  You’re signed in, but the CMS is for staff accounts. Ask an administrator for
                  access.
                </p>
              </ui-notice>
            }
          }
        </div>

        <form method="post" novalidate (ngSubmit)="submit()">
          <!-- Also disabled while signing in, for the reason in reset-password.ts. -->
          <fieldset
            class="account-panel__fields"
            [disabled]="!ready() || status().kind === 'busy'"
          >
            <legend class="sr-only">Your staff account</legend>
            <ui-field label="Email" required [error]="emailError()">
              <input
                #emailInput
                uiControl
                type="email"
                name="email"
                autocomplete="username"
                spellcheck="false"
                [(ngModel)]="email"
              />
            </ui-field>
            <ui-field label="Password" required [error]="passwordError()">
              <input
                #passwordInput
                uiControl
                type="password"
                name="password"
                autocomplete="current-password"
                [(ngModel)]="password"
              />
            </ui-field>
            <p>
              <button uiButton type="submit" variant="dark" [busy]="status().kind === 'busy'">
                {{ status().kind === 'busy' ? 'Signing in…' : 'Sign in' }}
              </button>
            </p>
          </fieldset>
        </form>
        <p class="account-panel__aside">
          <a routerLink="/account/forgot-password">Forgot your password?</a>
        </p>
    </app-account-panel>
  `,
})
export class Signin {
  /** From the query string (`withComponentInputBinding`). Untrusted: always read through `safeReturnUrl`. */
  readonly returnUrl = input<unknown>();

  private readonly sessions = inject(BffSessionService);
  private readonly location = inject(BrowserLocation);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly ready = signal(false);
  protected readonly status = signal<Status>({ kind: 'idle' });
  protected readonly signedInAs = signal<Session | null>(null);
  /** Set when a finished emailed link sent the person here (ADR 0029); read once, from the navigation. */
  protected readonly notice = signal<SigninNotice | null>(null);
  private readonly arrival = arrivedWith(inject(Router));
  private readonly panel = viewChild.required(AccountPanel);
  protected readonly destination = computed(() => safeReturnUrl(this.returnUrl()));

  private readonly emailInput = viewChild.required<ElementRef<HTMLInputElement>>('emailInput');
  private readonly passwordInput =
    viewChild.required<ElementRef<HTMLInputElement>>('passwordInput');

  /** Recomputed on submit, from the plain fields, so errors appear only once someone has tried. */
  protected readonly emailError = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  protected readonly rateLimitedMessage = computed(() => {
    const status = this.status();
    if (status.kind !== 'rate-limited' || status.minutes === null) {
      return 'Wait a few minutes before trying again.';
    }
    return status.minutes <= 1
      ? 'Wait a minute before trying again.'
      : `Wait ${status.minutes} minutes before trying again.`;
  });

  constructor() {
    inject(Seo).set({
      title: 'Staff sign-in',
      description: 'Sign in to edit the 6311arts website.',
      path: '/signin',
      noindex: true,
    });

    // A sign-in page is never cached, by a browser or by anything between: it answers per person, and a cached
    // copy would outlive a deploy that changed how sign-in works.
    noStore();

    afterNextRender(async () => {
      // Set after the first render, not in it: a live region announces what changes inside it, not what it was
      // born with. Focus goes to the heading, so the notice is read in context after the route change.
      if (this.arrival) {
        this.notice.set(this.arrival);
        this.panel().focusHeading();
      }
      this.ready.set(true);
      const session = await this.sessions.current();
      if (session && (!isCmsPath(this.destination()) || isStaff(session))) {
        this.signedInAs.set(session);
      }
    });
  }

  protected async submit(): Promise<void> {
    if (this.status().kind === 'busy') return;

    const email = this.email().trim();
    this.emailError.set(email.includes('@') ? null : 'Enter the email address you sign in with.');
    this.passwordError.set(this.password() ? null : 'Enter your password.');

    if (this.emailError()) return this.emailInput().nativeElement.focus();
    if (this.passwordError()) return this.passwordInput().nativeElement.focus();

    this.notice.set(null);
    this.status.set({ kind: 'busy' });
    const result = await this.sessions.signIn(email, this.password());

    if (result.kind === 'signed-in') {
      const destination = this.destination();
      if (isCmsPath(destination) && !isStaff(result.session)) {
        this.password.set('');
        this.status.set({ kind: 'not-staff', name: result.session.displayName });
        return;
      }
      this.password.set('');
      this.location.assign(destination);
      return;
    }

    // Never kept after a failure: the person retypes it, and it does not sit in memory meanwhile.
    this.password.set('');
    this.status.set(
      result.kind === 'rate-limited'
        ? {
            kind: 'rate-limited',
            minutes:
              result.retryAfterSeconds === null ? null : Math.ceil(result.retryAfterSeconds / 60),
          }
        : result,
    );
    this.passwordInput().nativeElement.focus();
  }
}
