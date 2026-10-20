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
  template: `
    @if (icon(); as name) {
      <ui-icon class="ui-empty__icon" [name]="name" [size]="48" [stroke]="1.2" />
    }
    @if (level() === 3) {
      <h3 class="ui-empty__title">{{ heading() }}</h3>
    } @else {
      <h2 class="ui-empty__title">{{ heading() }}</h2>
    }
    <div class="ui-empty__text"><ng-content /></div>
    <div class="ui-empty__actions"><ng-content select="[uiEmptyActions]" /></div>
  `,
  styles: `
    .ui-empty {
      display: grid;
      justify-items: center;
      gap: var(--space-3);
      padding: var(--space-7) 0;
      text-align: center;
      font-size: var(--fs-lead);
      color: var(--muted);
    }
    .ui-empty__icon {
      margin-bottom: var(--space-1);
    }
    .ui-empty__title {
      margin: 0;
      font-size: var(--fs-meta);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-wide);
      text-transform: uppercase;
      color: var(--ink);
    }
    .ui-empty__text {
      max-width: 52ch;
    }
    .ui-empty__text > :last-child {
      margin-bottom: 0;
    }
    .ui-empty__text:empty,
    .ui-empty__actions:empty {
      display: none;
    }
    .ui-empty__actions,
    .ui-empty__actions > [uiEmptyActions] {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--space-3);
    }
    .ui-empty__actions {
      margin-top: var(--space-2);
    }
  `,
})
export class UiEmptyState {
  readonly heading = input.required<string>();
  readonly icon = input<IconName>();
  /** Heading level inside the page outline. */
  readonly level = input<2 | 3>(2);
}
