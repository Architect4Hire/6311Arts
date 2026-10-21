import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { UiNotice } from '@6311/ui';
import { ToastService } from '../toast.service';

/**
 * Renders the toasts. The container is in the page from the start and is the live region, because screen
 * readers announce reliably only what changes inside a region that already exists (see `ui-notice`).
 */
@Component({
  selector: 'cms-toast-host',
  imports: [UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'cms-toasts', 'aria-live': 'polite', 'aria-relevant': 'additions' },
  templateUrl: './toast-host.html',
  styleUrl: './toast-host.scss',
})
export class ToastHost {
  protected readonly toasts = inject(ToastService);
}
