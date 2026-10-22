import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { formatDateRange } from '../../../core/time/gallery-time';
import { PublishStatus, STATUS_CHIPS } from '../../../models/content';
import {
  EXHIBITION_LIMITS,
  ExhibitionListQuery,
  ExhibitionSummary,
} from '../../../models/exhibition';
import { ExhibitionService } from '../../../services/exhibition.service';
import {
  DeletedRecords,
  DeletedRecordsSource,
} from '../../../shared/deleted-records/deleted-records';
import { SessionService } from '../../../core/session/session.service';
import { ROLES } from '../../../models/session';

type Filter = 'all' | PublishStatus | 'deleted';

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All exhibitions' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },

  // Admins only (ADR 0052): the same people who can delete and restore.
  { value: 'deleted', label: 'Deleted' },
];

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No exhibitions to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'exhibition' : 'exhibitions'}.`;
}

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

/**
 * Every show (`/admin/exhibitions`), newest first, with when it hangs, what state it is in and how many artists it
 * credits. Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-exhibition-list',
  imports: [
    RouterLink,
    UiButton,
    UiControl,
    UiEmptyState,
    UiField,
    UiNotice,
    UiStatusChip,
    DeletedRecords,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exhibition-list.html',
  styleUrl: './exhibition-list.scss',
})
export class ExhibitionList {
  private readonly exhibitions = inject(ExhibitionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly session = inject(SessionService);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly filters = computed(() =>
    FILTERS.filter((option) => option.value !== 'deleted' || this.isAdmin()),
  );
  protected readonly showingDeleted = computed(() => this.filter() === 'deleted');

  /** Where the shared Deleted view reads its rows and how it puts one back (ADR 0052). */
  protected readonly deletedSource: DeletedRecordsSource = {
    list: (query) =>
      this.exhibitions.listDeleted({ cursor: query.cursor ?? null, search: query.search }).pipe(
        map((page) => ({
          items: page.items.map((row) => ({ ...row, label: row.title })),
          nextCursor: page.nextCursor,
        })),
      ),
    restore: (id, etag) => this.exhibitions.restore(id, etag),
  };
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  /** The search as last sent: `search` settles into it after a pause in typing. */
  protected readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly ExhibitionSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  /** A new filter or search is loading over rows already shown: they stay put rather than vanishing. */
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  constructor() {
    effect(() => {
      this.filter();
      this.query();
      untracked(() => void this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' && this.query() === ''
      ? 'No exhibitions yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No exhibition’s title contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add the first show with “New exhibition”.'
      : `No exhibition is ${this.filter().toLowerCase()}.`;
  }

  protected dates(exhibition: ExhibitionSummary): string {
    return formatDateRange(exhibition.startDate, exhibition.endDate);
  }

  protected updated(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? '—' : UPDATED.format(date);
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
    }, SEARCH_DELAY_MS);
  }

  async reload(): Promise<void> {
    // The shared Deleted view fetches and announces its own rows, so the live list stays where it is.
    if (this.showingDeleted()) return;

    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.exhibitions.list(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.next.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(countOf(page.items.length, page.nextCursor !== null));
    } catch {
      if (generation === this.generation) this.state.set('failed');
    } finally {
      if (generation === this.generation) this.refreshing.set(false);
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.next();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.exhibitions.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'exhibition' : 'exhibitions'} loaded.`,
      );
      // "Load more" may be gone now; focus goes to the first exhibition added, where reading continues.
      afterNextRender(
        () =>
          this.host.nativeElement.querySelector<HTMLElement>(`a[data-row="${firstNew}"]`)?.focus(),
        { injector: this.injector },
      );
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  private currentQuery(): ExhibitionListQuery {
    const filter = this.filter();
    const search = this.query();
    return {
      limit: PAGE_SIZE,
      ...(filter === 'all' || filter === 'deleted' ? {} : { status: filter }),
      ...(search ? { search } : {}),
    };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
