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
} from '../../models/redirect';
import { RedirectService } from '../../services/redirect.service';

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

/**
 * Every redirect (`/admin/redirects`, Admins only), by old address: where each leads, how, and who made it. Filter
 * by source, search either address, and open one to edit or delete it.
 */
@Component({
  selector: 'cms-redirect-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div>
        <h1 class="cms-title">Redirects</h1>
        <p class="cms-lead">
          Old addresses and where they now lead. Changing a published page’s address adds one here
          by itself.
        </p>
      </div>
      <a uiButton variant="dark" routerLink="/redirects/new">New redirect</a>
    </div>

    <div class="tools">
      <ui-field label="Find an address" class="tools__search">
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
      <div class="filters" role="group" aria-label="Filter by source">
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

    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading redirects…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The redirects couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state icon="arrow" [heading]="emptyHeading()">
            <p>{{ emptyText() }}</p>
          </ui-empty-state>
        } @else {
          <div class="scroll" role="region" aria-label="Redirects" tabindex="0">
            <table class="redirects" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Redirects, by old address
              </caption>
              <thead>
                <tr>
                  <th scope="col">Old address</th>
                  <th scope="col">Leads to</th>
                  <th scope="col" class="wide">Type</th>
                  <th scope="col" class="wide">Made by</th>
                </tr>
              </thead>
              <tbody>
                @for (redirect of items(); track redirect.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/redirects', redirect.id]" [attr.data-row]="index">{{
                        redirect.fromPath
                      }}</a>
                    </th>
                    <td class="address">{{ redirect.toPath }}</td>
                    <td class="wide">
                      {{ redirect.statusCode === 301 ? 'Permanent' : 'Temporary' }}
                    </td>
                    <td class="wide">
                      <ui-status-chip [tone]="chips[redirect.source].tone" [icon]="chips[redirect.source].icon">{{
                        chips[redirect.source].label
                      }}</ui-status-chip>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More redirects couldn’t be loaded">
              <p>The redirects above are still here.</p>
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
    .redirects {
      width: 100%;
      border-collapse: collapse;
    }
    .redirects th,
    .redirects td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .redirects thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .redirects tbody th,
    .address {
      overflow-wrap: anywhere;
    }
    .more {
      text-align: center;
    }
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
    }
  `,
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
