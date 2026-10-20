import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UiButton, UiNotice } from '@6311/ui';
import { AccountService } from '../core/account.service';
import { LinkCredentials, LinkFragment } from '../core/link-fragment';
import { noStore } from '../core/no-store';
import { Seo } from '../core/seo.service';
import { goToSignin } from '../core/signin-notice';
import { AccountPanel } from '../shared/account-panel';
import { EmailRequestForm } from '../shared/email-request-form';

type Status =
  | { readonly kind: 'confirming' }
  | { readonly kind: 'link-invalid' }
  | { readonly kind: 'rate-limited'; readonly minutes: number | null }
  | { readonly kind: 'unavailable' };

/**
 * `/account/confirm-email` (ADR 0028, 0029): the page an emailed confirmation link opens, `#userId=…&token=…`.
 *
 * It confirms as soon as it runs in the browser, with no button: there is nothing to choose, and a link scanner
 * that executes the page can only confirm a mailbox it really reached. The credentials come from `main.ts`'s
 * `captureLinkFragment`, once, and are kept in a private field until they are spent or refused.
 *
 * Success sends the person to `/signin`. A link that cannot be used offers a new one in place
 * (`/bff/resend-confirmation`), with the same answer for every address.
 */
@Component({
  selector: 'app-confirm-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, UiButton, UiNotice, AccountPanel, EmailRequestForm],
  template: `
    <app-account-panel heading="Confirm your email address">
      <div class="account-panel__status" aria-live="polite">
        @switch (status().kind) {
          @case ('confirming') {
            <ui-notice tone="info" heading="Confirming your address…">
              <p>This takes a moment.</p>
            </ui-notice>
          }
          @case ('link-invalid') {
            <ui-notice tone="error" heading="This link can’t be used">
              <p>
                It may have expired (links work for one hour), been used already, or lost its ending
                on the way here. If you’ve already confirmed your address,
                <a routerLink="/signin">sign in</a>. Otherwise ask for a new link below.
              </p>
            </ui-notice>
          }
          @case ('rate-limited') {
            <ui-notice tone="error" heading="Too many attempts">
              <p>{{ rateLimitedMessage() }}</p>
            </ui-notice>
          }
          @case ('unavailable') {
            <ui-notice tone="error" heading="Your address isn’t confirmed yet">
              <p>
                Something on our side isn’t answering. Your link still works: try again shortly.
              </p>
            </ui-notice>
          }
        }
      </div>

      @switch (status().kind) {
        @case ('link-invalid') {
          <app-email-request-form kind="email-confirmation" submitLabel="Email me a new link" />
        }
      }
      <!-- One button for every retryable state, kept while the retry runs (busy), so focus is never dropped. -->
      @if (retryable()) {
        <p>
          <button
            uiButton
            type="button"
            variant="dark"
            [busy]="status().kind === 'confirming'"
            (click)="retry()"
          >
            {{ status().kind === 'confirming' ? 'Trying again…' : 'Try again' }}
          </button>
        </p>
      }
    </app-account-panel>
  `,
})
export class ConfirmEmail {
  private readonly account = inject(AccountService);
  private readonly fragment = inject(LinkFragment);
  private readonly router = inject(Router);

  /** The link's credentials: here, and in the one request body that uses them. */
  private link: LinkCredentials | null = null;

  protected readonly status = signal<Status>({ kind: 'confirming' });

  /** Set once a confirmation has failed for a reason worth retrying; from then on the button stays in the page. */
  private readonly retried = signal(false);
  protected readonly retryable = computed(() => {
    const kind = this.status().kind;
    return kind === 'rate-limited' || kind === 'unavailable' || (kind === 'confirming' && this.retried());
  });

  private readonly panel = viewChild.required(AccountPanel);
  private readonly injector = inject(Injector);

  protected readonly rateLimitedMessage = computed(() => {
    const status = this.status();
    if (status.kind !== 'rate-limited' || status.minutes === null) {
      return 'Wait a few minutes, then try again. Your link still works.';
    }
    return status.minutes <= 1
      ? 'Wait a minute, then try again. Your link still works.'
      : `Wait ${status.minutes} minutes, then try again. Your link still works.`;
  });

  constructor() {
    inject(Seo).set({
      title: 'Confirm your email address',
      description: 'Confirm the email address on your 6311arts account.',
      path: '/account/confirm-email',
      noindex: true,
    });
    noStore();

    afterNextRender(() => {
      this.link = this.fragment.take();
      void this.confirm();
    });
  }

  private focusHeadingAfterRender(): void {
    afterNextRender(() => this.panel().focusHeading(), { injector: this.injector });
  }

  protected retry(): Promise<void> {
    this.retried.set(true);
    return this.confirm();
  }

  private async confirm(): Promise<void> {
    const link = this.link;
    if (!link) {
      this.status.set({ kind: 'link-invalid' });
      return;
    }

    this.status.set({ kind: 'confirming' });
    const result = await this.account.confirmEmail(link);

    switch (result.kind) {
      case 'done':
        this.link = null;
        await goToSignin(this.router, 'email-confirmed');
        return;
      case 'link-invalid':
        this.link = null;
        this.status.set(result);
        // The Try-again button, if it had focus, has just gone.
        if (this.retried()) this.focusHeadingAfterRender();
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
}
