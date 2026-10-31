import {
  DestroyRef,
  Directive,
  ElementRef,
  Injector,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { IconName } from '@6311/ui';
import { CursorPage } from '../models/decode';
import { DeletedRecordsSource } from './deleted-records/deleted-records';
import { ListSort, SortColumn, SortDirection } from './list-sort';

/** One of a list's filter buttons. */
export interface ListFilter<F extends string> {
  readonly value: F;
  readonly label: string;
}

/** What every list request carries; a type's own query adds its filters. */
export interface ListRequest {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly sort?: string;
  readonly search?: string;
}

/** What one list is about; everything else a list does is the same for every type. */
export interface RecordListConfig<T, F extends string, Q extends ListRequest> {
  /** How counts name one row: "artist". */
  readonly noun: string;
  /** How counts name the rows: "artists", "pieces". */
  readonly plural: string;
  /** What the list as a whole is called, where that is not the plural: "artwork", "membership tiers". */
  readonly collection?: string;
  /** "Loading artists…", unless the list is called something else. */
  readonly loading?: string;
  /** "The artists couldn’t be loaded", unless the list is called something else. */
  readonly failed?: string;
  /** The empty state's glyph. */
  readonly icon: IconName;
  readonly list: (query: Q) => Observable<CursorPage<T>>;
  /** The columns a header sorts by (ADR 0062), the order a fresh visit shows, and its words if not a column's. */
  readonly sorts: readonly SortColumn[];
  readonly sort: { readonly column: string; readonly direction: SortDirection };
  readonly sortWords?: string;
  readonly filters: readonly ListFilter<F>[];
  /** The filter a fresh visit shows. */
  readonly filter: F;
  /** What a filter adds to the request; nothing for the one that shows everything. */
  readonly filterQuery: (filter: F) => Partial<Q>;
}

/** What `<cms-list-tools>` and `<cms-list-body>` show of a list, and what they can ask of it. */
export interface ListView {
  readonly plural: string;
  readonly loading: string;
  readonly failed: string;
  readonly icon: IconName;
  readonly sort: ListSort;
  readonly filters: Signal<readonly ListFilter<string>[]>;
  readonly filter: Signal<string>;
  readonly search: Signal<string>;
  readonly state: Signal<'loading' | 'failed' | 'ready'>;
  readonly empty: Signal<boolean>;
  /** Rows to show: the projected table is there. */
  readonly rows: Signal<boolean>;
  readonly refreshing: Signal<boolean>;
  readonly hasMore: Signal<boolean>;
  readonly loadingMore: Signal<boolean>;
  readonly moreFailed: Signal<boolean>;
  readonly liveMessage: Signal<string>;
  readonly emptyHeading: Signal<string>;
  readonly emptyText: Signal<string>;
  /** The shared Deleted view, for a type that has one (ADR 0052). */
  readonly deleted: {
    readonly showing: Signal<boolean>;
    readonly noun: string;
    readonly plural: string;
    readonly source: DeletedRecordsSource;
    readonly search: Signal<string>;
  } | null;
  setFilter(value: string): void;
  onSearch(value: string): void;
  retry(): void;
  more(): void;
}

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

/**
 * The list every CMS section shares: filter buttons, a debounced search, a sortable table kept in `?sort=`, cursor
 * paging with "Load more", and the counts read out as they change. A type's list supplies its columns, its rows'
 * cells and its {@link RecordListConfig}.
 */
@Directive()
export abstract class RecordList<
  T extends { readonly id: string },
  F extends string,
  Q extends ListRequest,
> {
  protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filter: WritableSignal<F>;
  protected readonly search = signal('');
  /** The search as last sent: `search` settles into it after a pause in typing. */
  protected readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly T[]>([]);
  private readonly cursor = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  /** A new filter or search is loading over rows already shown: they stay put rather than vanishing. */
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  /** The order, kept in the address bar as ?sort= so Back returns to it. */
  protected readonly sort: ListSort;

  /** What the list as a whole is called. */
  protected get collection(): string {
    return this.config.collection ?? this.config.plural;
  }

  /** Rows to show: the table is rendered (and projected into `<cms-list-body>`) only then. */
  protected readonly rows = computed(
    () => this.state() === 'ready' && this.items().length > 0 && !this.hidden(),
  );

  readonly view: ListView;

  constructor(protected readonly config: RecordListConfig<T, F, Q>) {
    this.filter = signal<F>(config.filter);
    this.sort = new ListSort(config.sorts, config.sort, config.sortWords);
    this.view = {
      plural: config.plural,
      loading: config.loading ?? `Loading ${this.collection}…`,
      failed: config.failed ?? `The ${this.collection} couldn’t be loaded`,
      icon: config.icon,
      sort: this.sort,
      filters: computed(() => this.visibleFilters()),
      filter: this.filter,
      search: this.search,
      state: this.state,
      empty: computed(() => this.items().length === 0),
      rows: this.rows,
      refreshing: this.refreshing,
      hasMore: computed(() => this.cursor() !== null),
      loadingMore: this.loadingMore,
      moreFailed: this.moreFailed,
      liveMessage: this.liveMessage,
      emptyHeading: computed(() => this.emptyHeading()),
      emptyText: computed(() => this.emptyText()),
      deleted: this.deletedView(),
      setFilter: (value) => this.setFilter(value as F),
      onSearch: (value) => this.onSearch(value),
      retry: () => void this.reload(),
      more: () => void this.more(),
    };

    // Whatever the request reads — filter, search, sort and a type's own filters — reloads the list when it changes.
    effect(() => {
      const request = this.request();
      untracked(() => void this.reload(request));
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected abstract emptyHeading(): string;
  protected abstract emptyText(): string;

  /** The filter buttons this viewer gets; all of them by default. */
  protected visibleFilters(): readonly ListFilter<F>[] {
    return this.config.filters;
  }

  /** The shared Deleted view, for a type that has one. */
  protected deletedView(): ListView['deleted'] {
    return null;
  }

  /** Another view is showing instead of the list (the Deleted view), so nothing is fetched or shown. */
  protected hidden(): boolean {
    return false;
  }

  /** What a type's own filters add to the request (the shop's category). */
  protected extraQuery(): Partial<Q> {
    return {};
  }

  async reload(request: Q = this.request()): Promise<void> {
    // Another view fetches and announces its own rows, so the live list stays where it is.
    if (this.hidden()) return;

    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.config.list(request));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.cursor.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(
        this.sort.announcement() + this.countOf(page.items.length, page.nextCursor !== null),
      );
    } catch {
      if (generation === this.generation) this.state.set('failed');
    } finally {
      if (generation === this.generation) this.refreshing.set(false);
    }
  }

  protected setFilter(value: F): void {
    this.filter.set(value);
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
    }, SEARCH_DELAY_MS);
  }

  protected async more(): Promise<void> {
    const cursor = this.cursor();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.config.list({ ...this.request(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.cursor.set(page.nextCursor);
      const count = page.items.length;
      this.liveMessage.set(
        `${count} more ${count === 1 ? this.config.noun : this.config.plural} loaded.`,
      );
      // "Load more" may be gone now; focus goes to the first row added, where reading continues.
      afterNextRender(
        () =>
          this.host.nativeElement.querySelector<HTMLElement>(`[data-row="${firstNew}"]`)?.focus(),
        { injector: this.injector },
      );
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  /** Writes to the live region. The same words twice still change it, so they are read out again. */
  protected announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private request(): Q {
    const search = this.query();
    return {
      sort: this.sort.param(),
      limit: PAGE_SIZE,
      ...this.config.filterQuery(this.filter()),
      ...(search ? { search } : {}),
      ...this.extraQuery(),
    } as Q;
  }

  private countOf(count: number, more: boolean): string {
    if (count === 0) return `No ${this.collection} to show.`;
    const what = count === 1 ? this.config.noun : this.config.plural;
    return `${more ? 'Showing the first ' : ''}${count} ${what}.`;
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}

/** A list date (the day a row was last changed), on the gallery's calendar. */
const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

export function updatedOn(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : UPDATED.format(date);
}
