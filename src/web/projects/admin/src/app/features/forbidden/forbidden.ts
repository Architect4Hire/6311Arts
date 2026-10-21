import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiEmptyState } from '@6311/ui';

/**
 * Signed in, but this account may not open what was asked for. Never a redirect to sign-in: that would say "your
 * session failed" when what happened is "your account may not do this" (ADR 0024).
 */
@Component({
  selector: 'cms-forbidden',
  imports: [RouterLink, UiButton, UiEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forbidden.html',
})
export class Forbidden {}
