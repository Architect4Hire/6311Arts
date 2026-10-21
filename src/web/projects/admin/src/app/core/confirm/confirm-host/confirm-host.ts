import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { UiButton, UiDialog } from '@6311/ui';
import { ConfirmService } from '../confirm.service';

/** The one dialog `ConfirmService` asks its questions in. Mounted once, at the root. */
@Component({
  selector: 'cms-confirm-host',
  imports: [UiButton, UiDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-host.html',
})
export class ConfirmHost {
  protected readonly confirm = inject(ConfirmService);
  protected readonly request = this.confirm.request;
  protected readonly open = computed(() => this.request() !== null);
}
