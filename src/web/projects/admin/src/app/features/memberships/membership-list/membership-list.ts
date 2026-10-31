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
import { PublishStatus, STATUS_CHIPS } from '../../../models/content';
import {
  MEMBERSHIP_LIMITS,
  MembershipPlanListQuery,
  MembershipPlanSummary,
} from '../../../models/membership';
import { MembershipService } from '../../../services/membership.service';
import {
  DeletedRecords,
  DeletedRecordsSource,
} from '../../../shared/deleted-records/deleted-records';
import { SessionService } from '../../../core/session/session.service';
import { ROLES } from '../../../models/session';
import { ListSort, SortColumn } from '../../../shared/list-sort';
import { SortHeader } from '../../../shared/sort-header/sort-header';
import { SortPicker } from '../../../shared/sort-picker/sort-picker';
import { Sortable } from '../../../shared/sortable';

type Filter = 'all' | PublishStatus | 'deleted';

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All tiers' },
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
  if (count === 0) return 'No membership tiers to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'tier' : 'tiers'}.`;
}

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

/** The columns a header sorts by (ADR 0062); the API names the same ones. */
const SORTS: readonly SortColumn[] = [
  { key: 'tier', label: 'Tier', kind: 'text' },
  { key: 'status', label: 'Status', kind: 'status' },
  { key: 'price', label: 'Price', kind: 'number' },
  { key: 'discount', label: 'Class discount', kind: 'number' },
  { key: 'updated', label: 'Updated', kind: 'date' },
];

/**
 * The membership tiers (`/admin/memberships`) in the order the site shows them, with what state each is in and what
 * Square says it bills. Filter by status, search by name, and open one to edit it.
 */
@Component({
  selector: 'cms-membership-list',
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
    DeletedRecords,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './membership-list.html',
  styleUrl: './membership-list.scss',
})
export class MembershipList {
  private readonly memberships = inject(MembershipService);
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
      this.memberships.listDeleted({ cursor: query.cursor ?? null, search: query.search }).pipe(
        map((page) => ({
          items: page.items.map((row) => ({ ...row, label: row.name })),
          nextCursor: page.nextCursor,
        })),
      ),
    restore: (id, etag) => this.memberships.restore(id, etag),
  };
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = MEMBERSHIP_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  protected readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly MembershipPlanSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  /** The order, kept in the address bar as ?sort= so Back returns to it. */
  protected readonly sort = new ListSort(
    SORTS,
    { column: 'order', direction: 'asc' },
    'the order the site shows them in',
  );

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
      ? 'No membership tiers yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No tier’s name contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add the first tier with “New tier”.'
      : `No tier is ${this.filter().toLowerCase()}.`;
  }

  protected perkCount(plan: MembershipPlanSummary): string {
    return plan.perkCount === 1 ? '1 perk' : `${plan.perkCount} perks`;
  }

  /** What the tier bills: Square's figure once it has reported one, the CMS's draft price until then. */
  protected price(plan: MembershipPlanSummary): string {
    const cents = plan.priceCents ?? plan.startingPriceCents;
    return cents === null ? '—' : money(cents, plan.currency);
  }

  protected priceSource(plan: MembershipPlanSummary): string {
    if (plan.priceCents === null) return 'Not in Square yet';
    return plan.cadence === null ? 'In Square' : `In Square · ${cadenceWords(plan.cadence)}`;
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
      const page = await firstValueFrom(this.memberships.list(this.currentQuery()));
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
      const page = await firstValueFrom(this.memberships.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'tier' : 'tiers'} loaded.`,
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

  private currentQuery(): MembershipPlanListQuery {
    const filter = this.filter();
    const search = this.query();
    return {
      sort: this.sort.param(),
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

function money(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

/** Square's own cadence value (`ANNUAL`, `EVERY_TWO_MONTHS`) in words. */
export function cadenceWords(cadence: string): string {
  const words = cadence.toLowerCase().replace(/_/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}
