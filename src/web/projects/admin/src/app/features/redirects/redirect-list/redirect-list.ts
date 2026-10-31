import {
  ChangeDetectionStrategy,
  Component,
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
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import {
  REDIRECT_LIMITS,
  Redirect,
  RedirectListQuery,
  RedirectSource,
  SOURCE_CHIPS,
} from '../../../models/redirect';
import { RedirectService } from '../../../services/redirect.service';
import { ListSort, SortColumn } from '../../../shared/list-sort';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { SortPicker } from '../../../shared/sort-picker/sort-picker';
import { Sortable } from '../../../shared/sortable';

type Filter = 'all' | RedirectSource;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All redirects' },
  { value: 'Manual', label: 'Added by staff' },
  { value: 'SlugChange', label: 'Address changes' },
  { value: 'Imported', label: 'From the old site' },
];

const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No redirects to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'redirect' : 'redirects'}.`;
}

/** The columns a header sorts by (ADR 0062); the API names the same ones. */
const SORTS: readonly SortColumn[] = [
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
];

/**
 * Every redirect (`/admin/redirects`, Admins only), by old address: where each leads, how, and who made it. Filter
 * by source, search either address, and open one to edit or delete it.
 */
@Component({
  selector: 'cms-redirect-list',
  imports: [
    Sortable,
    SortHeader,
    SortPicker,
    RouterLink,
    UiButton,
    UiControl,
    UiEmptyState,
    UiField,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './redirect-list.html',
  styleUrl: './redirect-list.scss',
})
export class RedirectList {
  private readonly redirects = inject(RedirectService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = SOURCE_CHIPS;
  protected readonly limits = REDIRECT_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly Redirect[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  /** The order, kept in the address bar as ?sort= so Back returns to it. */
  protected readonly sort = new ListSort(SORTS, { column: 'from', direction: 'asc' });

  constructor() {
    effect(() => {
      this.filter();
      this.query();
      this.sort.current();
      untracked(() => void this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
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

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
    }, SEARCH_DELAY_MS);
  }

  async reload(): Promise<void> {
    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.redirects.list(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.next.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(
        this.sort.announcement() + countOf(page.items.length, page.nextCursor !== null),
      );
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
      const page = await firstValueFrom(this.redirects.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'redirect' : 'redirects'} loaded.`,
      );
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

  private currentQuery(): RedirectListQuery {
    const filter = this.filter();
    const search = this.query();
    return {
      sort: this.sort.param(),
      limit: PAGE_SIZE,
      ...(filter === 'all' ? {} : { source: filter }),
      ...(search ? { search } : {}),
    };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
