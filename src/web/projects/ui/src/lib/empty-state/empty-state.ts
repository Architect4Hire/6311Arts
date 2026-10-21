import { ChangeDetectionStrategy, Component, input, ViewEncapsulation } from '@angular/core';
import { IconName, UiIcon } from '../icon/icon';

/**
 * What a list or search shows when it has nothing, after the spike's `.empty`: quiet, centred, muted.
 * Project a sentence of explanation, and actions marked `uiEmptyActions`.
 *
 * ```html
 * <ui-empty-state icon="frame" heading="No artists yet">
 *   <p>Artists you add appear here and on the public roster once published.</p>
 *   <div uiEmptyActions><a uiButton routerLink="new">Add an artist</a></div>
 * </ui-empty-state>
 * ```
 */
@Component({
  selector: 'ui-empty-state',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-empty' },
  templateUrl: './empty-state.html',
  styleUrl: './empty-state.scss',
})
export class UiEmptyState {
  readonly heading = input.required<string>();
  readonly icon = input<IconName>();
  /** Heading level inside the page outline. */
  readonly level = input<2 | 3>(2);
}
