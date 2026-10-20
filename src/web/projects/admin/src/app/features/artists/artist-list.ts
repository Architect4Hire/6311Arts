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
  ARTIST_LIMITS,
  ArtistListQuery,
  ArtistSummary,
  PublishStatus,
  STATUS_CHIPS,
} from '../../models/artist';
import { ArtistService } from '../../services/artist.service';

type Filter = 'all' | PublishStatus;

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All artists' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

const PAGE_SIZE = 50;

function countOf(count: number, more: boolean): string {
  if (count === 0) return 'No artists to show.';
  return `${more ? 'Showing the first ' : ''}${count} ${count === 1 ? 'artist' : 'artists'}.`;
}

const UPDATED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeZone: 'America/Chicago',
});

/**
 * The roster (`/admin/artists`): every artist by name, with what state each is in. Filter by status, search by
 * name, and open one to edit it.
 */
@Component({
  selector: 'cms-artist-list',
  imports: [RouterLink, UiButton, UiControl, UiEmptyState, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div>
        <h1 class="cms-title">Artists</h1>
        <p class="cms-lead">
          The artist roster: who shows at 6311arts, what they make, and their portrait.
        </p>
      </div>
      <a uiButton variant="dark" routerLink="/artists/new">New artist</a>
    </div>

    <div class="tools">
      <ui-field label="Find an artist" class="tools__search">
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
        <p role="status">Loading artists…</p>
      }
      @case ('failed') {
        <ui-notice tone="error" heading="The artists couldn’t be loaded">
          <p>6311arts may be unreachable for a moment.</p>
          <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (items().length === 0) {
          <ui-empty-state icon="brush" [heading]="emptyHeading()">
            <p>{{ emptyText() }}</p>
          </ui-empty-state>
        } @else {
          @if (refreshing()) {
            <p role="status" class="refreshing">Updating the list…</p>
          }
          <div class="scroll" role="region" aria-label="Artists" tabindex="0">
            <table class="roster" [attr.aria-busy]="refreshing() || null">
              <caption class="sr-only">
                Artists, by name
              </caption>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col" class="wide">Medium</th>
                  <th scope="col">Status</th>
                  <th scope="col">Showing</th>
                  <th scope="col" class="wide">Updated</th>
                </tr>
              </thead>
              <tbody>
                @for (artist of items(); track artist.id; let index = $index) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/artists', artist.id]" [attr.data-row]="index">{{
                        artist.name
                      }}</a>
                      <span class="slug">/artists/{{ artist.slug }}</span>
                      @if (artist.medium) {
                        <span class="narrow-medium">{{ artist.medium }}</span>
                      }
                    </th>
                    <td class="wide">{{ artist.medium ?? '—' }}</td>
                    <td>
                      <ui-status-chip
                        [tone]="chips[artist.status].tone"
                        [icon]="chips[artist.status].icon"
                        >{{ chips[artist.status].label }}</ui-status-chip
                      >
                    </td>
                    <td>{{ artist.active ? 'Yes' : 'No' }}</td>
                    <td class="wide">
                      <time [attr.datetime]="artist.updatedAt">{{ updated(artist.updatedAt) }}</time>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (moreFailed()) {
            <ui-notice tone="error" heading="More artists couldn’t be loaded">
              <p>The artists above are still here.</p>
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
    .roster {
      width: 100%;
      border-collapse: collapse;
    }
    .roster th,
    .roster td {
      padding: var(--space-2) var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .roster thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .roster tbody th {
      font-weight: var(--fw-bold);
    }
    .slug {
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
    .narrow-medium {
      display: none;
      font-weight: normal;
    }
    /* On a phone, medium moves under the name and the date goes, so nothing hides off to the side. */
    @media (max-width: 600px) {
      .wide {
        display: none;
      }
      .narrow-medium {
        display: block;
      }
    }
  `,
})
export class ArtistList {
  private readonly artists = inject(ArtistService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = ARTIST_LIMITS;
  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  /** The search as last sent: `search` settles into it after a pause in typing. */
  private readonly query = signal('');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly ArtistSummary[]>([]);
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
      ? 'No artists yet'
      : 'Nothing to show here';
  }

  protected emptyText(): string {
    if (this.query() !== '') return `No artist’s name contains “${this.query()}”.`;
    return this.filter() === 'all'
      ? 'Add the first artist with “New artist”.'
      : `No artist is ${this.filter().toLowerCase()}.`;
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
      const page = await firstValueFrom(this.artists.list(this.currentQuery()));
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
      const page = await firstValueFrom(this.artists.list({ ...this.currentQuery(), cursor }));
      if (generation !== this.generation) return;
      const firstNew = this.items().length;
      this.items.update((items) => [...items, ...page.items]);
      this.next.set(page.nextCursor);
      this.liveMessage.set(`${page.items.length} more ${page.items.length === 1 ? 'artist' : 'artists'} loaded.`);
      // "Load more" may be gone now; focus goes to the first artist added, where reading continues.
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

  private currentQuery(): ArtistListQuery {
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
