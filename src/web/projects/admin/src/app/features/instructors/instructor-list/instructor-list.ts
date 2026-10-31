import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import {
  INSTRUCTOR_LIMITS,
  InstructorListQuery,
  InstructorSummary,
  STATUS_CHIPS,
} from '../../../models/instructor';
import { InstructorService } from '../../../services/instructor.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

/**
 * The instructors (`/admin/instructors`, ADR 0043): everyone who teaches, by name, with what state each is in. Filter
 * by status, search by name, and open one to edit it.
 */
@Component({
  selector: 'cms-instructor-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './instructor-list.html',
  styleUrl: './instructor-list.scss',
})
export class InstructorList extends ContentList<InstructorSummary, InstructorListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = INSTRUCTOR_LIMITS;
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(InstructorService),
      noun: 'instructor',
      plural: 'instructors',
      icon: 'people',
      field: 'name',
      firstHint: 'Add the first instructor with “New instructor”.',
      sorts: [
        { key: 'name', label: 'Name', kind: 'text' },
        { key: 'medium', label: 'Medium', kind: 'text' },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'teaching', label: 'Teaching', kind: 'flag' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'name', direction: 'asc' },
    });
  }
}
