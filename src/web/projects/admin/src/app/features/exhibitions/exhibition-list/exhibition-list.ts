import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import { formatDateRange } from '../../../core/time/gallery-time';
import { STATUS_CHIPS } from '../../../models/content';
import {
  EXHIBITION_LIMITS,
  ExhibitionListQuery,
  ExhibitionSummary,
} from '../../../models/exhibition';
import { ExhibitionService } from '../../../services/exhibition.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/**
 * Every show (`/admin/exhibitions`), newest first, with when it hangs, what state it is in and how many artists it
 * credits. Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-exhibition-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exhibition-list.html',
  styleUrl: './exhibition-list.scss',
})
export class ExhibitionList extends ContentList<ExhibitionSummary, ExhibitionListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(ExhibitionService),
      noun: 'exhibition',
      plural: 'exhibitions',
      icon: 'frame',
      field: 'title',
      firstHint: 'Add the first show with “New exhibition”.',
      sorts: [
        { key: 'title', label: 'Title', kind: 'text' },
        {
          key: 'dates',
          label: 'Dates',
          kind: 'date',
          words: { asc: 'earliest opening first', desc: 'latest opening first' },
        },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'artists', label: 'Artists', kind: 'number' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'dates', direction: 'desc' },
    });
  }

  protected dates(exhibition: ExhibitionSummary): string {
    return formatDateRange(exhibition.startDate, exhibition.endDate);
  }
}
