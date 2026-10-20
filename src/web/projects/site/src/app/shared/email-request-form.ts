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
import { UiButton, UiControl, UiField, UiNotice } from '@6311/ui';
import { AccountEmailKind, AccountEmailResult, AccountService } from '../core/account.service';

type Status = { readonly kind: 'idle' } | { readonly kind: 'busy' } | AccountEmailResult;

/** The gateway's ceiling on an address (`BffEndpoints.AccountLimits.EmailMaxLength`). */
const EMAIL_MAX_LENGTH = 256;

/**
 * "Send me a link": one address, for a password reset or a new confirmation link (ADR 0028, 0029). Used by
 * `/account/forgot-password`, and in place by the reset and confirmation pages when their link cannot be used.
 *
 * **Every well-formed address gets the same answer,** whether it is registered, unknown or already confirmed: the
 * gateway answers 202 before anything that knows has run, and the wording below promises nothing that would differ.
 *
 * Like `/signin`, the field stays disabled until the app runs in the browser, and the form carries `method="post"`,
 * so a native submit before hydration could never put the address in a URL.
 */
@Component({
  selector: 'app-email-request-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, UiButton, UiControl, UiField, UiNotice],
  template: `
    <div class="account-panel__status" aria-live="polite">
      @switch (status().kind) {
        @case ('busy') {
          <p class="sr-only">Sending…</p>
        }
        @case ('sent') {
          <ui-notice tone="success" heading="Check your email">
            <p>{{ sentMessage() }}</p>
          </ui-notice>
        }
        @case ('rate-limited') {
          <ui-notice tone="error" heading="Too many requests">
            <p>{{ rateLimitedMessage() }}</p>
          </ui-notice>
        }
        @case ('unavailable') {
          <ui-notice tone="error" heading="We couldn’t send the link">
            <p>Something on our side isn’t answering. Try again shortly.</p>
          </ui-notice>
        }
      }
    </div>

    <form method="post" novalidate (ngSubmit)="submit()">
      <fieldset class="account-panel__fields" [disabled]="!ready()">
        <legend class="sr-only">{{ legend() }}</legend>
        <ui-field label="Email" required [error]="emailError()">
          <input
            #emailInput
            uiControl
            type="email"
            name="email"
            autocomplete="username"
            spellcheck="false"
            [attr.maxlength]="maxLength"
            [(ngModel)]="email"
          />
        </ui-field>
        <p>
          <button uiButton type="submit" variant="dark" [busy]="status().kind === 'busy'">
            {{ status().kind === 'busy' ? 'Sending…' : submitLabel() }}
          </button>
        </p>
      </fieldset>
    </form>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
  `,
})
export class EmailRequestForm {
  readonly kind = input.required<AccountEmailKind>();
  readonly submitLabel = input('Email me a link');

  private readonly account = inject(AccountService);

  protected readonly maxLength = EMAIL_MAX_LENGTH;
  protected readonly email = signal('');
  protected readonly ready = signal(false);
  protected readonly status = signal<Status>({ kind: 'idle' });
  protected readonly emailError = signal<string | null>(null);

  private readonly emailInput = viewChild.required<ElementRef<HTMLInputElement>>('emailInput');

  protected readonly legend = computed(() =>
    this.kind() === 'password-reset'
      ? 'Request a password reset link'
      : 'Request a new confirmation link',
  );

  /** The same words for every address: nothing here may depend on whether the address has an account. */
  protected readonly sentMessage = computed(() =>
    this.kind() === 'password-reset'
      ? 'If an account uses that address, we’ve sent it a link to choose a new password. The link works for one hour. Nothing there? Check your spam folder, or ask again in a few minutes.'
      : 'If that address has an account waiting to be confirmed, we’ve sent it a new link. The link works for one hour. If you’ve already confirmed your address, you can sign in.',
  );

  protected readonly rateLimitedMessage = computed(() => {
    const status = this.status();
    if (status.kind !== 'rate-limited' || status.retryAfterSeconds === null) {
      return 'Wait a few minutes before asking again.';
    }
    const minutes = Math.ceil(status.retryAfterSeconds / 60);
    return minutes <= 1
      ? 'Wait a minute before asking again.'
      : `Wait ${minutes} minutes before asking again.`;
  });

  constructor() {
    afterNextRender(() => this.ready.set(true));
  }

  protected async submit(): Promise<void> {
    if (this.status().kind === 'busy') return;

    const email = this.email().trim();
    if (!email.includes('@') || email.length > EMAIL_MAX_LENGTH) {
      this.emailError.set('Enter the email address you sign in with.');
      this.emailInput().nativeElement.focus();
      return;
    }
    this.emailError.set(null);

    this.status.set({ kind: 'busy' });
    const result = await this.account.requestEmail(this.kind(), email);

    if (result.kind === 'invalid-address') {
      this.status.set({ kind: 'idle' });
      this.emailError.set('Enter the email address you sign in with.');
      this.emailInput().nativeElement.focus();
      return;
    }
    this.status.set(result);
  }
}
