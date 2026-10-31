import { Directive, computed, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { SessionService } from '../core/session/session.service';
import { PublishStatus } from '../models/content';
import { CursorPage } from '../models/decode';
import { ROLES } from '../models/session';
import { DeletedRecord } from './deleted-records/deleted-records';
import { ListFilter, ListRequest, ListView, RecordList, RecordListConfig } from './record-list';

/** A content list's filter: everything, one status, or (Admins) the Deleted view. */
export type ContentFilter = 'all' | PublishStatus | 'deleted';

/** A content list's request: the record list's, plus a status. */
export interface ContentListRequest extends ListRequest {
  readonly status?: PublishStatus;
}

/** A Deleted view row as a type's service returns it: named by its name or its title. */
type DeletedRow = Omit<DeletedRecord, 'label'> & {
  readonly name?: string;
  readonly title?: string;
};

/** The reads a content list makes, as each type's service already offers them. */
export interface ContentListApi<T, Q> {
  list(query: Q): Observable<CursorPage<T>>;
  listDeleted(query: {
    cursor?: string | null;
    search?: string;
  }): Observable<{ readonly items: readonly DeletedRow[]; readonly nextCursor: string | null }>;
  restore(id: string, etag: string): Observable<unknown>;
}

/** A publishable type's list config: the record list's, with the filters every content list shares. */
export interface ContentListConfig<T, Q extends ContentListRequest> extends Omit<
  RecordListConfig<T, ContentFilter, Q>,
  'list' | 'filters' | 'filter' | 'filterQuery'
> {
  readonly api: ContentListApi<T, Q>;
  /** Whether a record can be scheduled, so "Scheduled" is a filter of its own. */
  readonly scheduled?: boolean;
  /** What a record is called and what the search looks in: its name or its title. */
  readonly field: 'name' | 'title';
  /** The empty state with nothing filtered: "Add the first artist with “New artist”." */
  readonly firstHint: string;
  /** How the Deleted view names its rows, where that differs from the list ("plan"). */
  readonly deletedNoun?: { readonly one: string; readonly many: string };
}

/**
 * The list every publishable type shares: a {@link RecordList} filtered by status, with the shared Deleted view for
 * Admins — the same people who can delete and restore (ADR 0052).
 */
@Directive()
export abstract class ContentList<
  T extends { readonly id: string },
  Q extends ContentListRequest,
> extends RecordList<T, ContentFilter, Q> {
  private readonly session = inject(SessionService);
  private readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));

  protected readonly showingDeleted = computed(() => this.filter() === 'deleted');

  constructor(content: ContentListConfig<T, Q>) {
    super({
      ...content,
      list: (query) => content.api.list(query),
      filter: 'all',
      filters: [
        { value: 'all', label: `All ${content.collection ?? content.plural}` },
        { value: 'Draft', label: 'Drafts' },
        ...(content.scheduled ? [{ value: 'Scheduled' as const, label: 'Scheduled' }] : []),
        { value: 'Published', label: 'Published' },
        { value: 'Archived', label: 'Archived' },
        { value: 'deleted', label: 'Deleted' },
      ],
      filterQuery: (filter) =>
        (filter === 'all' || filter === 'deleted' ? {} : { status: filter }) as Partial<Q>,
    });
  }

  /** The config as the content list was given it (the record list keeps it). */
  private get content(): ContentListConfig<T, Q> {
    return this.config as unknown as ContentListConfig<T, Q>;
  }

  protected override visibleFilters(): readonly ListFilter<ContentFilter>[] {
    return this.config.filters.filter((option) => option.value !== 'deleted' || this.isAdmin());
  }

  protected override hidden(): boolean {
    return this.filter() === 'deleted';
  }

  /** Called while the record list is being built: it reads only the config, never this class's own fields. */
  protected override deletedView(): ListView['deleted'] {
    const content = this.content;
    return {
      showing: computed(() => this.filter() === 'deleted'),
      noun: content.deletedNoun?.one ?? content.noun,
      plural: content.deletedNoun?.many ?? content.plural,
      search: this.query,
      source: {
        list: (query) =>
          content.api.listDeleted({ cursor: query.cursor ?? null, search: query.search }).pipe(
            map((page) => ({
              items: page.items.map((row) => ({ ...row, label: row[content.field] ?? '' })),
              nextCursor: page.nextCursor,
            })),
          ),
        restore: (id, etag) => content.api.restore(id, etag),
      },
    };
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' && this.query() === '' && !this.extraFilter()
      ? `No ${this.collection} yet`
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    const query = this.query();
    if (query !== '') return `No ${this.config.noun}’s ${this.content.field} contains “${query}”.`;
    const extra = this.extraFilter();
    if (extra) return extra;
    return this.filter() === 'all'
      ? this.content.firstHint
      : `No ${this.config.noun} is ${this.filter().toLowerCase()}.`;
  }

  /** What a type's own filter says when it leaves nothing to show (the shop's category), or `null`. */
  protected extraFilter(): string | null {
    return null;
  }
}
