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
import { GALLERY_TIME_ZONE } from '../../core/time/gallery-time';
import { PublishStatus, STATUS_CHIPS } from '../../models/content';
import {
  SHOP_CATEGORIES,
  SHOP_LIMITS,
  ShopItemSummary,
  ShopListQuery,
  categoryLabel,
} from '../../models/shop';
import { ShopService } from '../../services/shop.service';

type Filter = 'all' | PublishStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All artwork' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Scheduled', label: 'Scheduled' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No artwork to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'piece' : 'pieces'}.`;
}

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: GALLERY_TIME_ZONE,
});

/**
 * Every piece in the shop (`/admin/shop`), by title, with who made it, what it is filed under, and what Square says
 * about its price and stock. Filter by status or category, search by title, and open one to edit it.
 *
 * Merch is not here: it is sold at the front desk through Square POS and never enters the CMS (ADR 0032).
 */
@Component({
  selector: 'cms-shop-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div>
        <h1 class="cms-title">Shop</h1>
        <p class="cms-lead">
          The artwork the shop sells. Prices and stock are Square’s record; merch is sold at the
          front desk.
        </p>
      </div>
      <a uiButton variant="dark" routerLink="/shop/new">New piece</a>
    </div>

    <div class="tools">
      <ui-field label="Find a piece" class="tools__search">
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
      <ui-field label="Category" class="tools__category">
        <select
          #categorySelect
          uiControl
          name="category"
          [value]="category()"
          (change)="category.set(categorySelect.value)"
        >
          <option value="">Every category</option>
          @for (option of categories; track option.slug) {
            <option [value]="option.slug">{{ option.label }}</option>
          }
        </select>
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
        <p role="status">Loading artwork…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The shop couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state icon="cart" [heading]="emptyHeading()">
            <p>{{ emptyText() }}</p>
          </ui-empty-state>
        } @else {
          @if (refreshing()) {
            <p role="status" class="refreshing">Updating the list…</p>
          }
          <div class="scroll" role="region" aria-label="Artwork" tabindex="0">
            <table class="pieces" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Artwork, by title
              </caption>
              <thead>
                <tr>
                  <th scope="col">Title</th>
                  <th scope="col">Artist</th>
                  <th scope="col">Status</th>
                  <th scope="col">In Square</th>
                  <th scope="col" class="wide">Updated</th>
                </tr>
              </thead>
              <tbody>
                @for (piece of items(); track piece.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/shop', piece.id]" [attr.data-row]="index">{{
                        piece.title
                      }}</a>
                      <span class="meta">{{ meta(piece) }}</span>
                    </th>
                    <td>{{ piece.artist?.name || '—' }}</td>
                    <td>
                      <ui-status-chip
                        [tone]="chips[piece.status].tone"
                        [icon]="chips[piece.status].icon"
                        >{{ chips[piece.status].label }}</ui-status-chip
                      >
                    </td>
                    <td>
                      @if (piece.priceCents !== null || piece.stockOnHand !== null) {
                        <span class="square">{{ square(piece) }}</span>
                        @if (piece.soldOut) {
                          <ui-status-chip tone="attention" icon="stop">Sold out</ui-status-chip>
                        }
                      } @else {
                        <span class="square square--none">Not in Square yet</span>
                      }
                    </td>
                    <td class="wide">
                      <time [attr.datetime]="piece.updatedAt">{{ updated(piece.updatedAt) }}</time>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More artwork couldn’t be loaded">
              <p>The pieces above are still here.</p>
            </ui-notice>
          }
          @if (nextCursor()) {
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
      flex: 1 1 240px;
      max-width: 420px;
    }
    .tools__category {
      flex: 0 1 200px;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .scroll {
      overflow-x: auto;
    }
    .pieces {
      width: 100%;
      border-collapse: collapse;
    }
    .pieces th,
    .pieces td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .pieces thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .pieces tbody th {
      font-weight: var(--fw-bold);
    }
    .meta {
      display: block;
      font-size: var(--fs-small);
      font-weight: normal;
      color: var(--text-soft);
      overflow-wrap: anywhere;
    }
    .square {
      display: block;
      font-size: var(--fs-small);
    }
    .square--none {
      color: var(--text-soft);
    }
    .more {
      text-align: center;
    }
    .refreshing {
      margin: 0;
      color: var(--text-soft);
    }
    /* On a phone the date goes; Square's figures stay, because they are what staff scan for, and the table
       scrolls. */
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
    }
  `,
})
export class ShopList {
  private readonly shop = inject(ShopService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly categories = SHOP_CATEGORIES;
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = SHOP_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly category = signal('');
  protected readonly search = signal('');
  /** The search as last sent: `search` settles into it after a pause in typing. */
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly ShopItemSummary[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  /** A new filter or search is loading over rows already shown: they stay put rather than vanishing. */
  protected readonly refreshing = signal(false);
  protected readonly liveMessage = signal('');

  constructor() {
    effect(() => {
      this.filter();
      this.category();
      this.query();
      untracked(() => void this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected emptyHeading(): string {
    return this.filter() === 'all' && this.query() === '' && this.category() === ''
      ? 'No artwork yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No piece’s title contains “${this.query()}”.`;
    if (this.category() !== '') return `Nothing is filed under ${categoryLabel(this.category())}.`;
    return this.filter() === 'all'
      ? 'Add the first piece with “New piece”.'
      : `No piece is ${this.filter().toLowerCase()}.`;
  }

  /** The line under the title: its SKU, its categories, and whether there is only one of it. */
  protected meta(piece: ShopItemSummary): string {
    return [
      piece.sku,
      piece.categories.map(categoryLabel).join(', '),
      piece.oneOfAKind ? 'One of a kind' : null,
    ]
      .filter(Boolean)
      .join(' · ');
  }

  /** What Square says about the piece, in the words staff scan for. */
  protected square(piece: ShopItemSummary): string {
    const price =
      piece.priceCents === null
        ? null
        : new Intl.NumberFormat('en-US', { style: 'currency', currency: piece.currency }).format(
            piece.priceCents / 100,
          );
    const stock = piece.stockOnHand === null ? null : `${piece.stockOnHand} in stock`;
    return [price, stock].filter(Boolean).join(' · ');
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
      const page = await firstValueFrom(this.shop.list(this.currentQuery()));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.nextCursor.set(page.nextCursor);
      this.state.set('ready');
      this.liveMessage.set(countOf(page.items.length, page.nextCursor !== null));
    } catch {
      if (generation === this.generation) this.state.set('failed');
    } finally {
      if (generation === this.generation) this.refreshing.set(false);
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) return;

    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.shop.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.nextCursor.set(page.nextCursor);
      this.liveMessage.set(
        `${page.items.length} more ${page.items.length === 1 ? 'piece' : 'pieces'} loaded.`,
      );
      // "Load more" may be gone now; focus goes to the first piece added, where reading continues.
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

  private currentQuery(): ShopListQuery {
    const filter = this.filter();
    const search = this.query();
    const category = this.category();
    return {
      limit: PAGE_SIZE,
      ...(filter === 'all' ? {} : { status: filter }),
      ...(search ? { search } : {}),
      ...(category ? { category } : {}),
    };
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
