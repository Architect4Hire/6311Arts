import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { UiButton, UiDialog } from '@6311/ui';
import { ConfirmService } from './confirm.service';

/** The one dialog `ConfirmService` asks its questions in. Mounted once, at the root. */
@Component({
  selector: 'cms-confirm-host',
  imports: [UiButton, UiDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog
      [open]="open()"
      [heading]="request()?.heading ?? ''"
      (closed)="confirm.answer(false)"
    >
      <p>{{ request()?.message }}</p>
      <div uiDialogActions>
        <!-- autofocus: a question that can discard work opens on the safe answer, not the close button. -->
        <button uiButton type="button" variant="border-dark" autofocus (click)="confirm.answer(false)">
          {{ request()?.cancelLabel }}
        </button>
        <button uiButton type="button" variant="dark" (click)="confirm.answer(true)">
          {{ request()?.confirmLabel }}
        </button>
      </div>
    </ui-dialog>
  `,
})
export class ConfirmHost {
  protected readonly confirm = inject(ConfirmService);
  protected readonly request = this.confirm.request;
  protected readonly open = computed(() => this.request() !== null);
}
