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
import { PublishStatus, STATUS_CHIPS } from '../../../models/content';
import { PAGE_LIMITS, PageListQuery, PageSummary } from '../../../models/page';
import { PageService } from '../../../services/page.service';

type Filter = 'all' | PublishStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All pages' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No pages to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'page' : 'pages'}.`;
}

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

/**
 * Standalone pages (`/admin/pages`) by title — privacy, terms and the site's other copy — with what state each is in.
 * Filter by status, search by title, and open one to edit it.
 */
@Component({
  selector: 'cms-page-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-list.html',
  styleUrl: './page-list.scss',
})
export class PageList {
  private readonly pages = inject(PageService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = PAGE_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly PageSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
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
    return this.filter() === 'all' && this.query() === '' ? 'No pages yet' : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No page’s title contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add the first page with “New page”.'
      : `No page is ${this.filter().toLowerCase()}.`;
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
    const generation = ++this.generation;
    const showing = this.state() === 'ready' && this.items().length > 0;
    if (showing) this.refreshing.set(true);
    else this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.pages.list(this.currentQuery()));
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
      const page = await firstValueFrom(this.pages.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'page' : 'pages'} loaded.`,
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

  private currentQuery(): PageListQuery {
    const filter = this.filter();
    const search = this.query();
    return {
      limit: PAGE_SIZE,
      ...(filter === 'all' ? {} : { status: filter }),
      ...(search ? { search } : {}),
    };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
