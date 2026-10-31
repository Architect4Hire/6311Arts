import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import {
  SUBMISSION_CHIPS,
  SubmissionListQuery,
  SubmissionStatus,
  SubmissionSummary,
  wantLabel,
} from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { RecordList } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';
import { FormsTabs, formatInstant } from '../forms-tabs/forms-tabs';

type Filter = 'all' | SubmissionStatus;

/**
 * The artist submissions inbox (`/admin/forms/submissions`), newest first: who wrote, what they want, how far
 * through the inbox each is, and how many pictures came with it. Filter by status and open one to read it.
 */
@Component({
  selector: 'cms-submission-list',
  imports: [FormsTabs, ListBody, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submission-list.html',
  styleUrl: './submission-list.scss',
})
export class SubmissionList extends RecordList<SubmissionSummary, Filter, SubmissionListQuery> {
  protected readonly chips = SUBMISSION_CHIPS;
  protected readonly received = formatInstant;

  constructor() {
    const forms = inject(FormsService);
    super({
      list: (query) => forms.listSubmissions(query),
      noun: 'submission',
      plural: 'submissions',
      icon: 'mail',
      filters: [
        { value: 'all', label: 'All' },
        { value: 'New', label: 'New' },
        { value: 'Reviewed', label: 'Reviewed' },
        { value: 'Accepted', label: 'Accepted' },
        { value: 'Declined', label: 'Declined' },
      ],
      filter: 'all',
      filterQuery: (filter) => (filter === 'all' ? {} : { status: filter }),
      sorts: [
        { key: 'name', label: 'Name', kind: 'text' },
        { key: 'email', label: 'Email', kind: 'text' },
        { key: 'wants', label: 'Wants to', kind: 'text' },
        {
          key: 'status',
          label: 'Status',
          kind: 'status',
          words: { asc: 'new first', desc: 'declined first' },
        },
        { key: 'pictures', label: 'Pictures', kind: 'number' },
        { key: 'received', label: 'Received', kind: 'date' },
      ],
      sort: { column: 'received', direction: 'desc' },
    });
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' ? 'No submissions yet' : 'Nothing to show here';
  }

  protected emptyText(): string {
    return this.filter() === 'all'
      ? 'Artists’ submissions from the site’s form arrive here.'
      : 'No submissions have this status.';
  }

  protected wants(submission: SubmissionSummary): string {
    return submission.wants.length === 0 ? '—' : submission.wants.map(wantLabel).join(', ');
  }
}
