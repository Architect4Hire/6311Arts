import { ChangeDetectionStrategy, Component, input, ViewEncapsulation } from '@angular/core';
import { IconName, UiIcon } from '../icon/icon';

/** `neutral` informs, `positive` confirms, `pending` waits (dashed rule), `attention` needs a person. */
export type StatusTone = 'neutral' | 'positive' | 'pending' | 'attention';

/**
 * A small uppercase status label sized like the spike's `.tag`, but outlined on white rather than solid ink: a
 * tag labels content for visitors, a status chip reports system state to staff. Status is never colour alone,
 * so a glyph is required and the word is the projected content. It wraps rather than overflowing a narrow cell.
 *
 * ```html
 * <ui-status-chip tone="positive" icon="check">Published</ui-status-chip>
 * ```
 */
@Component({
  selector: 'ui-status-chip',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-status', '[attr.data-tone]': 'tone()' },
  templateUrl: './status-chip.html',
  styleUrl: './status-chip.scss',
})
export class UiStatusChip {
  readonly tone = input<StatusTone>('neutral');
  readonly icon = input.required<IconName>();
}
