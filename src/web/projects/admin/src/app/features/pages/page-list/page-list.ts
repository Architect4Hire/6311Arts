import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UiNotice, UiStatusChip } from '@6311/ui';
import { STATUS_CHIPS } from '../../../models/content';
import { PAGE_DELETED_STATE, PAGE_LIMITS, PageListQuery, PageSummary } from '../../../models/page';
import { PageService } from '../../../services/page.service';
import { ContentFilter, ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/** The router state the editor set, when it is there and is what it should be. */
function titleOf(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

/**
 * Standalone pages (`/admin/pages`) by title — privacy, terms and the site's other copy — with what state each is in.
 * Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-page-list',
  imports: [
    ListBody,
    ListHead,
    ListTools,
    RouterLink,
    SortHeader,
    Sortable,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-list.html',
  styleUrl: './page-list.scss',
})
export class PageList extends ContentList<PageSummary, PageListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = PAGE_LIMITS;
  protected readonly updated = updatedOn;

  /** The title the editor's delete sent on its way back here, shown until the next list action. */
  protected readonly justDeleted = signal(
    titleOf(inject(Router).currentNavigation()?.extras.state?.[PAGE_DELETED_STATE]),
  );

  constructor() {
    super({
      api: inject(PageService),
      noun: 'page',
      plural: 'pages',
      icon: 'info',
      field: 'title',
      firstHint: 'Add the first page with “New page”.',
      sorts: [
        { key: 'title', label: 'Title', kind: 'text' },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'title', direction: 'asc' },
    });
  }

  /** Changing what the list shows clears the one-off messages above it. */
  protected override setFilter(value: ContentFilter): void {
    this.justDeleted.set(null);
    super.setFilter(value);
  }

  protected override onSearch(value: string): void {
    this.justDeleted.set(null);
    super.onSearch(value);
  }
}
