import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiButton, UiControl, UiField } from '@6311/ui';
import { ListView } from '../record-list';

/**
 * A list's search and filter buttons. A type's own controls (the shop's category, the newsletter's export) are
 * projected between the two.
 *
 * ```html
 * <cms-list-tools [view]="view" searchLabel="Find an artist" [searchMax]="limits.searchMaxLength" />
 * ```
 */
@Component({
  selector: 'cms-list-tools',
  imports: [UiButton, UiControl, UiField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './list-tools.html',
  styleUrl: './list-tools.scss',
})
export class ListTools {
  readonly view = input.required<ListView>();
  /** The search field's label; a list without one has no search. */
  readonly searchLabel = input<string | null>(null);
  readonly searchMax = input<number | null>(null);
  /** What the filter buttons filter by, for a screen reader. */
  readonly filtersLabel = input('Filter the list');
}
