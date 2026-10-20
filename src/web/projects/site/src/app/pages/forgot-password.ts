import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { noStore } from '../core/no-store';
import { Seo } from '../core/seo.service';
import { AccountPanel } from '../shared/account-panel';
import { EmailRequestForm } from '../shared/email-request-form';

/**
 * `/account/forgot-password` (ADR 0028, 0029): ask for a reset link by address. Reached from `/signin`, and from a
 * reset link that can no longer be used.
 *
 * The answer is the same for a registered and an unregistered address, by construction: the gateway is always 202,
 * and answers before anything that knows has run.
 */
@Component({
  selector: 'app-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, AccountPanel, EmailRequestForm],
  template: `
    <app-account-panel
      heading="Reset your password"
      lead="Enter the email address you sign in with, and we’ll send you a link to choose a new password."
    >
      <app-email-request-form kind="password-reset" />
      <p class="account-panel__aside"><a routerLink="/signin">Back to sign-in</a></p>
    </app-account-panel>
  `,
})
export class ForgotPassword {
  constructor() {
    inject(Seo).set({
      title: 'Reset your password',
      description: 'Ask for a link to choose a new 6311arts password.',
      path: '/account/forgot-password',
      noindex: true,
    });
    noStore();
  }
}
