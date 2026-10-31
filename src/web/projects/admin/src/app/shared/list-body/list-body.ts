import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiButton, UiEmptyState, UiNotice } from '@6311/ui';
import { DeletedRecords } from '../deleted-records/deleted-records';
import { ListView } from '../record-list';
import { SortPicker } from '../sort-picker/sort-picker';

/**
 * Everything a list shows below its tools, the same for every type: the sort picker, the live region, loading /
 * failed / empty, the shared Deleted view, "Load more". The table itself is projected, and rendered by the list only
 * while it has rows; a notice for the screen (a delete just made, an export refused) goes in with `listNotice`.
 *
 * ```html
 * <cms-list-body [view]="view">
 *   @if (rows()) { <div class="scroll">…</div> }
 * </cms-list-body>
 * ```
 */
@Component({
  selector: 'cms-list-body',
  imports: [DeletedRecords, SortPicker, UiButton, UiEmptyState, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './list-body.html',
  styleUrl: './list-body.scss',
})
export class ListBody {
  readonly view = input.required<ListView>();
}
