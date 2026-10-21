import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { UiButton, UiNotice } from '@6311/ui';
import { SessionService } from '../../core/session/session.service';

/**
 * The gateway could not say whether this session is good — the API restarting, usually. The session is kept
 * (ADR 0024: "an unreachable API refuses the request but keeps the ticket"), so trying again is the fix.
 */
@Component({
  selector: 'cms-unavailable',
  imports: [UiButton, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './unavailable.html',
})
export class Unavailable {
  /** The CMS URL the visit was for (from the query string), to resume once the session answers. */
  readonly from = input<string>();

  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  protected readonly retrying = signal(false);

  protected async retry(): Promise<void> {
    this.retrying.set(true);
    await this.session.reload();
    this.retrying.set(false);
    // Only ever a path inside this app: the router resolves it against the /admin/ base.
    const target = this.from();
    await this.router.navigateByUrl(
      target?.startsWith('/') && !target.startsWith('//') ? target : '/',
    );
  }
}
