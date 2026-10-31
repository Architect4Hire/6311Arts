import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import {
  REDIRECT_LIMITS,
  Redirect,
  RedirectListQuery,
  RedirectSource,
  SOURCE_CHIPS,
} from '../../../models/redirect';
import { RedirectService } from '../../../services/redirect.service';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { RecordList } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

type Filter = 'all' | RedirectSource;

/**
 * Every redirect (`/admin/redirects`, Admins only), by old address: where each leads, how, and who made it. Filter
 * by source, search either address, and open one to edit or delete it.
 */
@Component({
  selector: 'cms-redirect-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './redirect-list.html',
  styleUrl: './redirect-list.scss',
})
export class RedirectList extends RecordList<Redirect, Filter, RedirectListQuery> {
  protected readonly chips = SOURCE_CHIPS;
  protected readonly limits = REDIRECT_LIMITS;

  constructor() {
    const redirects = inject(RedirectService);
    super({
      list: (query) => redirects.list(query),
      noun: 'redirect',
      plural: 'redirects',
      icon: 'arrow',
      filters: [
        { value: 'all', label: 'All redirects' },
        { value: 'Manual', label: 'Added by staff' },
        { value: 'SlugChange', label: 'Address changes' },
        { value: 'Imported', label: 'From the old site' },
      ],
      filter: 'all',
      filterQuery: (filter) => (filter === 'all' ? {} : { source: filter }),
      sorts: [
        { key: 'from', label: 'Old address', kind: 'text' },
        { key: 'to', label: 'Leads to', kind: 'text' },
        {
          key: 'type',
          label: 'Type',
          kind: 'number',
          words: { asc: 'permanent first', desc: 'temporary first' },
        },
        {
          key: 'source',
          label: 'Made by',
          kind: 'status',
          words: { asc: 'staff first', desc: 'web address changes first' },
        },
      ],
      sort: { column: 'from', direction: 'asc' },
    });
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' && this.query() === ''
      ? 'No redirects yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No redirect’s address contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add one with “New redirect” to send an old address somewhere new.'
      : 'No redirects of this kind.';
  }
}
