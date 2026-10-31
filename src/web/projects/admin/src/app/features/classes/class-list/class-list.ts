import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiStatusChip } from '@6311/ui';
import { GALLERY_TIME_ZONE } from '../../../core/time/gallery-time';
import { STATUS_CHIPS } from '../../../models/content';
import { CLASS_LIMITS, ClassListQuery, ClassSummary } from '../../../models/class';
import { ClassService } from '../../../services/class.service';
import { ContentList } from '../../../shared/content-list';
import { ListBody } from '../../../shared/list-body/list-body';
import { ListHead } from '../../../shared/list-head/list-head';
import { ListTools } from '../../../shared/list-tools/list-tools';
import { updatedOn } from '../../../shared/record-list';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { Sortable } from '../../../shared/sortable';

const NEXT = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: GALLERY_TIME_ZONE,
});

/**
 * Every class and workshop (`/admin/classes`), by title, with what it is, who teaches its runs, how many runs it has
 * and when it next meets (ADR 0044). Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-class-list',
  imports: [ListBody, ListHead, ListTools, RouterLink, SortHeader, Sortable, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './class-list.html',
  styleUrl: './class-list.scss',
})
export class ClassList extends ContentList<ClassSummary, ClassListQuery> {
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = CLASS_LIMITS;
  protected readonly updated = updatedOn;

  constructor() {
    super({
      api: inject(ClassService),
      noun: 'class',
      plural: 'classes',
      icon: 'calendar',
      field: 'title',
      firstHint: 'Add the first class with “New class”.',
      scheduled: true,
      sorts: [
        { key: 'title', label: 'Title', kind: 'text' },
        {
          key: 'next',
          label: 'Next session',
          kind: 'date',
          first: 'asc',
          words: { asc: 'soonest first', desc: 'furthest off first' },
        },
        { key: 'status', label: 'Status', kind: 'status' },
        { key: 'runs', label: 'Runs', kind: 'number' },
        { key: 'instructors', label: 'Instructors', kind: 'text' },
        { key: 'updated', label: 'Updated', kind: 'date' },
      ],
      sort: { column: 'title', direction: 'asc' },
    });
  }

  protected kind(offering: ClassSummary): string {
    return [offering.classType, offering.level].filter(Boolean).join(' · ');
  }

  protected next(iso: string | null): string {
    if (iso === null) return 'No sessions to come';
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? '—' : NEXT.format(date);
  }
}
