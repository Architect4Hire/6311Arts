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
import { PublishStatus, STATUS_CHIPS } from '../../models/content';
import {
  MEMBERSHIP_LIMITS,
  MembershipPlanListQuery,
  MembershipPlanSummary,
} from '../../models/membership';
import { MembershipService } from '../../services/membership.service';

type Filter = 'all' | PublishStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All tiers' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
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

/**
 * The membership tiers (`/admin/memberships`) in the order the site shows them, with what state each is in and what
 * Square says it bills. Filter by status, search by name, and open one to edit it.
 */
@Component({
  selector: 'cms-membership-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div>
        <h1 class="cms-title">Memberships</h1>
        <p class="cms-lead">
          The tiers on the memberships page. Billing and member status are managed in Square.
        </p>
      </div>
      <a uiButton variant="dark" routerLink="/memberships/new">New tier</a>
    </div>

    <div class="tools">
      <ui-field label="Find a tier" class="tools__search">
        <input
          #searchInput
          uiControl
          type="search"
          name="search"
          autocomplete="off"
          [attr.maxlength]="limits.searchMaxLength"
          [value]="search()"
          (input)="onSearch(searchInput.value)"
        />
      </ui-field>
      <div class="filters" role="group" aria-label="Filter by status">
        @for (option of filters; track option.value) {
          <button
            uiButton
            type="button"
            size="sm"
            [variant]="filter() === option.value ? 'dark' : 'border-dark'"
            [attr.aria-pressed]="filter() === option.value"
            (click)="filter.set(option.value)"
          >
            {{ option.label }}
          </button>
        }
      </div>
    </div>

    <!-- Exists from the start, so the counts written into it later are read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading membership tiers…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The membership tiers couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state icon="info" [heading]="emptyHeading()">
            <p>{{ emptyText() }}</p>
          </ui-empty-state>
        } @else {
          @if (refreshing()) {
            <p role="status" class="refreshing">Updating the list…</p>
          }
          <div class="scroll" role="region" aria-label="Membership tiers" tabindex="0">
            <table class="plans" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Membership tiers, in the order the site shows them
              </caption>
              <thead>
                <tr>
                  <th scope="col">Tier</th>
                  <th scope="col">Status</th>
                  <th scope="col">Price</th>
                  <th scope="col" class="wide">Class discount</th>
                  <th scope="col" class="wide">Updated</th>
                </tr>
              </thead>
              <tbody>
                @for (plan of items(); track plan.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/memberships', plan.id]" [attr.data-row]="index">{{
                        plan.name
                      }}</a>
                      @if (plan.featured) {
                        <span class="featured">Featured</span>
                      }
                      <span class="perks">{{ perkCount(plan) }}</span>
                    </th>
                    <td>
                      <ui-status-chip
                        [tone]="chips[plan.status].tone"
                        [icon]="chips[plan.status].icon"
                        >{{ chips[plan.status].label }}</ui-status-chip
                      >
                    </td>
                    <td>
                      {{ price(plan) }}
                      <span class="source">{{ priceSource(plan) }}</span>
                    </td>
                    <td class="wide">{{ plan.discountPercent }}%</td>
                    <td class="wide">
                      <time [attr.datetime]="plan.updatedAt">{{ updated(plan.updatedAt) }}</time>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More tiers couldn’t be loaded">
              <p>The tiers above are still here.</p>
            </ui-notice>
          }
          @if (next()) {
            <p class="more">
              <button uiButton type="button" [busy]="loadingMore()" (click)="more()">
                {{ moreFailed() ? 'Try loading more again' : 'Load more' }}
              </button>
            </p>
          }
        }
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
      justify-content: space-between;
    }
    .cms-lead {
      margin-bottom: 0;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
    }
    .tools__search {
      flex: 1 1 260px;
      max-width: 420px;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .scroll {
      overflow-x: auto;
    }
    .plans {
      width: 100%;
      border-collapse: collapse;
    }
    .plans th,
    .plans td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .plans thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .plans tbody th {
      font-weight: var(--fw-bold);
    }
    .featured {
      display: inline-block;
      margin-left: var(--space-2);
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .perks,
    .source {
      display: block;
      font-size: var(--fs-small);
      font-weight: normal;
      color: var(--text-soft);
      overflow-wrap: anywhere;
    }
    .more {
      text-align: center;
    }
    .refreshing {
      margin: 0;
      color: var(--text-soft);
    }
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
    }
  `,
})
export class MembershipList {
  private readonly memberships = inject(MembershipService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = MEMBERSHIP_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly MembershipPlanSummary[]>([]);
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
      const page = await firstValueFrom(
        this.memberships.list({ ...this.currentQuery(), cursor }),
      );
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

function money(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

/** Square's own cadence value (`ANNUAL`, `EVERY_TWO_MONTHS`) in words. */
export function cadenceWords(cadence: string): string {
  const words = cadence.toLowerCase().replace(/_/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}
