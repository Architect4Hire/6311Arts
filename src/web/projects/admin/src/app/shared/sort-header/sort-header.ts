import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { UiIcon } from '@6311/ui';
import { Sortable } from '../sortable';

/**
 * A sortable column header (ADR 0062), inside a `table[cmsSortable]`. The whole cell is one button: a click sorts by
 * the column, a second click turns it round. The sorted column says so through `aria-sort`, its chevron and the
 * rule under it; the others show which way a click would go when hovered or focused.
 */
@Component({
  selector: 'th[cmsSortHeader]',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'sort-header',
    '[class.sort-header--sorted]': 'direction() !== null',
    '[class.sort-header--descending]': 'shown() === "desc"',
    '[attr.aria-sort]': 'ariaSort()',
  },
  templateUrl: './sort-header.html',
  styleUrl: './sort-header.scss',
})
export class SortHeader {
  private readonly table = inject(Sortable);

  /** The API's name for the column. */
  readonly cmsSortHeader = input.required<string>();

  protected readonly direction = computed(() =>
    this.table.cmsSortable().directionOf(this.cmsSortHeader()),
  );

  /** The way the chevron points: the column's order when sorted, the order a click would give when not. */
  protected readonly shown = computed(
    () => this.direction() ?? this.table.cmsSortable().firstDirection(this.cmsSortHeader()),
  );

  protected readonly ariaSort = computed(() => {
    const direction = this.direction();
    return direction === null ? null : direction === 'asc' ? 'ascending' : 'descending';
  });

  protected toggle(): void {
    this.table.cmsSortable().toggle(this.cmsSortHeader());
  }
}
