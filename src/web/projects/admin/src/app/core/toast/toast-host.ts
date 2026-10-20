import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { UiNotice } from '@6311/ui';
import { ToastService } from './toast.service';

/**
 * Renders the toasts. The container is in the page from the start and is the live region, because screen
 * readers announce reliably only what changes inside a region that already exists (see `ui-notice`).
 */
@Component({
  selector: 'cms-toast-host',
  imports: [UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'cms-toasts', 'aria-live': 'polite', 'aria-relevant': 'additions' },
  template: `
    @for (toast of toasts.toasts(); track toast.id) {
      <ui-notice
        class="cms-toast"
        [tone]="toast.tone"
        [heading]="toast.heading"
        dismissible
        (dismissed)="toasts.dismiss(toast.id)"
      >
        @if (toast.message) {
          <p>{{ toast.message }}</p>
        }
        @if (toast.reference) {
          <p class="cms-toast__ref">
            Reference: <code>{{ toast.reference }}</code>
          </p>
        }
      </ui-notice>
    }
  `,
  styles: `
    :host {
      position: fixed;
      right: var(--space-5);
      bottom: var(--space-5);
      z-index: 50;
      display: grid;
      gap: var(--space-3);
      width: min(420px, calc(100vw - 2 * var(--space-5)));
    }
    :host:empty {
      display: none;
    }
    .cms-toast {
      box-shadow: 0 4px 18px var(--black-12);
    }
    .cms-toast__ref {
      color: var(--text-soft);
      font-size: var(--fs-label);
      overflow-wrap: anywhere;
    }
  `,
})
export class ToastHost {
  protected readonly toasts = inject(ToastService);
}
