import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { UiControl, UiField } from '@6311/ui';
import { ListSort, SortDirection, sortParam } from '../list-sort';

interface Choice {
  readonly value: string;
  readonly label: string;
}

/**
 * "Sort by" for a phone (ADR 0062). Under 600px a CMS table drops its wide columns and their headers with them, so
 * the order they sort by is offered here instead, every column each way round. Hidden on wider screens, where the
 * headers do it.
 */
@Component({
  selector: 'cms-sort-picker',
  imports: [UiControl, UiField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sort-picker.html',
  styleUrl: './sort-picker.scss',
})
export class SortPicker {
  readonly sort = input.required<ListSort>();

  protected readonly choices = computed<readonly Choice[]>(() => {
    const sort = this.sort();
    const choices = sort.columns.flatMap((column) => {
      const first = sort.firstDirection(column.key);
      const second: SortDirection = first === 'asc' ? 'desc' : 'asc';
      return [first, second].map((direction) => {
        const state = { column: column.key, direction };
        return { value: sortParam(state), label: sort.describe(state) };
      });
    });

    // A list whose own order is no column (a tier's display order) offers that first.
    const fallback = sort.fallback;
    return sort.columns.some((column) => column.key === fallback.column)
      ? choices
      : [{ value: sortParam(fallback), label: sort.describe(fallback) }, ...choices];
  });

  protected choose(value: string): void {
    const direction: SortDirection = value.startsWith('-') ? 'desc' : 'asc';
    this.sort().set({ column: direction === 'desc' ? value.slice(1) : value, direction });
  }
}
