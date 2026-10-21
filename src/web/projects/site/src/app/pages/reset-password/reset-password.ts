import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { UiButton, UiControl, UiField, UiNotice } from '@6311/ui';
import { AccountService } from '../../core/account.service';
import { LinkCredentials, LinkFragment } from '../../core/link-fragment';
import { noStore } from '../../core/no-store';
import { Seo } from '../../core/seo.service';
import { goToSignin } from '../../core/signin-notice';
import { AccountPanel } from '../../shared/account-panel/account-panel';
import { EmailRequestForm } from '../../shared/forms/email-request-form/email-request-form';

type Status =
  /** Server-rendered, or not yet hydrated: the link has not been read. */
  | { readonly kind: 'waiting' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'link-invalid' }
  | { readonly kind: 'rate-limited'; readonly minutes: number | null }
  | { readonly kind: 'unavailable' };

/** The gateway's ceiling on a password (`BffEndpoints.AccountLimits.PasswordMaxLength`). */
const PASSWORD_MAX_LENGTH = 256;

/** Identity's floor for everyone (`IdentityPolicy.PasswordMinLength`); staff need 14, which the API enforces. */
const PASSWORD_MIN_LENGTH = 8;

/**
 * `/account/reset-password` (ADR 0028, 0029): the page an emailed reset link opens, `#userId=…&token=…`.
 *
 * `main.ts` has already taken the user id and token out of the address bar before the app started
 * (`captureLinkFragment`); this page takes them from memory once, after the first render in the browser, and keeps
 * them in a private field — never a signal the template reads, never the DOM, storage or the console. They leave
 * only in the body of `POST /bff/reset-password`.
 *
 * Success sends the person to `/signin`. A link that cannot be used (expired, used, incomplete, or lost to a
 * refresh) offers a new one in place.
 */
@Component({
  selector: 'app-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, UiButton, UiControl, UiField, UiNotice, AccountPanel, EmailRequestForm],
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly account = inject(AccountService);
  private readonly fragment = inject(LinkFragment);
  private readonly router = inject(Router);

  /** The link's credentials: here, and in the one request body that uses them. */
  private link: LinkCredentials | null = null;

  protected readonly maxLength = PASSWORD_MAX_LENGTH;
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly status = signal<Status>({ kind: 'waiting' });
  protected readonly passwordError = signal<string | null>(null);
  protected readonly ready = computed(() => {
    const kind = this.status().kind;
    return kind !== 'waiting' && kind !== 'link-invalid';
  });

  private readonly passwordInput = viewChild<ElementRef<HTMLInputElement>>('passwordInput');
  private readonly panel = viewChild.required(AccountPanel);
  private readonly injector = inject(Injector);

  protected readonly rateLimitedMessage = computed(() => {
    const status = this.status();
    if (status.kind !== 'rate-limited' || status.minutes === null) {
      return 'Wait a few minutes before trying again. Your link still works.';
    }
    return status.minutes <= 1
      ? 'Wait a minute before trying again. Your link still works.'
      : `Wait ${status.minutes} minutes before trying again. Your link still works.`;
  });

  constructor() {
    inject(Seo).set({
      title: 'Choose a new password',
      description: 'Set a new password for your 6311arts account.',
      path: '/account/reset-password',
      noindex: true,
    });
    noStore();

    afterNextRender(() => {
      this.link = this.fragment.take();
      this.status.set(this.link ? { kind: 'ready' } : { kind: 'link-invalid' });
    });
  }

  protected async submit(): Promise<void> {
    const link = this.link;
    if (!link || this.status().kind === 'busy') return;

    const password = this.password();
    const error = !password
      ? 'Enter a new password.'
      : password.length < PASSWORD_MIN_LENGTH
        ? `Use at least ${PASSWORD_MIN_LENGTH} characters.`
        : password.length > PASSWORD_MAX_LENGTH
          ? `Use at most ${PASSWORD_MAX_LENGTH} characters.`
          : null;
    this.passwordError.set(error);
    if (error) return this.focusPassword();

    this.status.set({ kind: 'busy' });
    const result = await this.account.resetPassword(link, password);

    switch (result.kind) {
      case 'done':
        this.forget();
        await goToSignin(this.router, 'password-changed');
        return;
      case 'password-rejected':
        // Only reachable with a valid link, so the policy's reasons are this person's to see (ADR 0028).
        this.password.set('');
        this.passwordError.set(
          result.reasons.length
            ? result.reasons.join(' ')
            : 'That password can’t be used. Choose another.',
        );
        this.status.set({ kind: 'ready' });
        return this.focusPassword();
      case 'link-invalid':
        this.forget();
        this.status.set({ kind: 'link-invalid' });
        // The form, and the button that had focus, have just been replaced by the request form.
        afterNextRender(() => this.panel().focusHeading(), { injector: this.injector });
        return;
      case 'rate-limited':
        this.status.set({
          kind: 'rate-limited',
          minutes:
            result.retryAfterSeconds === null ? null : Math.ceil(result.retryAfterSeconds / 60),
        });
        return;
      case 'unavailable':
        this.status.set(result);
        return;
    }
  }

  /** The link is spent or refused: nothing to keep, and the password typed for it goes too. */
  private forget(): void {
    this.link = null;
    this.password.set('');
  }

  private focusPassword(): void {
    this.passwordInput()?.nativeElement.focus();
  }
}
