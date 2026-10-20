import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiStatusChip } from '@6311/ui';
import { ArtistSummary } from '../../models/artist';
import { STATUS_CHIPS } from '../../models/content';
import { CreditedArtist, EXHIBITION_LIMITS } from '../../models/exhibition';
import { ArtistService } from '../../services/artist.service';

/** How long typing pauses before the roster is searched. */
const SEARCH_DELAY_MS = 300;

/** How many matches to offer at once: enough to find a name, few enough to read. */
const RESULT_LIMIT = 8;

type SearchState = 'idle' | 'searching' | 'failed' | 'ready';

/**
 * An exhibition's credits (`<cms-artist-credits [(credits)]>`): the artists it shows, in the order the site lists
 * them, and a search of the roster to add more. Archived artists and those already credited are not offered.
 *
 * Ordering is by "Move up" / "Move down" buttons rather than drag and drop, so it works the same by keyboard, by
 * screen reader and by touch. Each button names its artist, and focus stays on the artist that moved.
 */
@Component({
  selector: 'cms-artist-credits',
  imports: [UiButton, UiControl, UiField, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="credits-heading">
      <h2 id="credits-heading" class="panel__title">Artists</h2>

      <!-- Exists from the start, so what is written into it later is read out. -->
      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

      @if (credits().length === 0) {
        <p class="panel__empty">No artists credited yet.</p>
      } @else {
        <ol class="credits" aria-label="Credited artists, in order">
          @for (
            artist of credits();
            track artist.id;
            let index = $index, first = $first, last = $last
          ) {
            <li class="credit" [attr.data-credit]="artist.id">
              <span class="credit__name">
                {{ artist.name }}
                @if (artist.status !== 'Published') {
                  <ui-status-chip
                    [tone]="chips[artist.status].tone"
                    [icon]="chips[artist.status].icon"
                    >{{ chips[artist.status].label }}</ui-status-chip
                  >
                }
              </span>
              <span class="credit__actions">
                <button
                  uiButton
                  type="button"
                  size="sm"
                  data-action="up"
                  [disabled]="disabled() || first"
                  [attr.aria-label]="'Move ' + artist.name + ' up'"
                  (click)="move(index, -1)"
                >
                  Up
                </button>
                <button
                  uiButton
                  type="button"
                  size="sm"
                  data-action="down"
                  [disabled]="disabled() || last"
                  [attr.aria-label]="'Move ' + artist.name + ' down'"
                  (click)="move(index, 1)"
                >
                  Down
                </button>
                <button
                  uiButton
                  type="button"
                  size="sm"
                  data-action="remove"
                  [disabled]="disabled()"
                  [attr.aria-label]="'Remove ' + artist.name"
                  (click)="remove(index)"
                >
                  Remove
                </button>
              </span>
            </li>
          }
        </ol>
      }

      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }

      @if (full()) {
        <p class="panel__hint">
          An exhibition credits at most {{ limits.artistsMaxCount }} artists. Remove one to add
          another.
        </p>
      } @else {
        <ui-field
          label="Add an artist"
          hint="Search the roster by name. Archived artists can’t be credited."
        >
          <input
            #searchInput
            uiControl
            type="search"
            name="artistSearch"
            autocomplete="off"
            [attr.maxlength]="limits.searchMaxLength"
            [disabled]="disabled()"
            [value]="search()"
            (input)="onSearch(searchInput.value)"
          />
        </ui-field>

        @switch (state()) {
          @case ('searching') {
            <p role="status" class="panel__hint">Searching the roster…</p>
          }
          @case ('failed') {
            <p class="error">
              The roster couldn’t be searched.
              <button uiButton type="button" size="sm" (click)="runSearch()">Try again</button>
            </p>
          }
          @case ('ready') {
            @if (offers().length === 0) {
              <p class="panel__hint">No artist on the roster to add matches “{{ query() }}”.</p>
            } @else {
              <ul class="offers" aria-label="Matching artists">
                @for (artist of offers(); track artist.id) {
                  <li class="offer">
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      [disabled]="disabled()"
                      [attr.aria-label]="'Add ' + artist.name"
                      (click)="add(artist)"
                    >
                      Add
                    </button>
                    <span>
                      {{ artist.name }}
                      @if (artist.medium) {
                        <span class="offer__medium">{{ artist.medium }}</span>
                      }
                    </span>
                  </li>
                }
              </ul>
            }
          }
        }
      }
    </section>
  `,
  styles: `
    .panel {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      border: var(--border-thin) solid var(--line);
    }
    .panel__title {
      margin: 0;
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .panel__empty,
    .panel__hint {
      margin: 0;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .credits,
    .offers {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .credits {
      counter-reset: credit;
    }
    .credit {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
      padding-bottom: var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      counter-increment: credit;
    }
    .credit__name {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      font-weight: var(--fw-bold);
      overflow-wrap: anywhere;
    }
    .credit__name::before {
      content: counter(credit) '.';
      font-weight: normal;
      color: var(--text-soft);
    }
    .credit__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    .offer {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
    }
    .offer__medium {
      display: block;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
  `,
})
export class ArtistCredits {
  /** The credits, in order. Two-way: every change is the editor's draft. */
  readonly credits = model.required<readonly CreditedArtist[]>();
  readonly disabled = input(false);
  /** The API's or the form's word on the credits, shown above the search. */
  readonly error = input<string | null>(null);

  private readonly artists = inject(ArtistService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bumped by every search, so an answer to an older one is dropped. */
  private generation = 0;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly search = signal('');
  /** The search as last sent. */
  protected readonly query = signal('');
  protected readonly state = signal<SearchState>('idle');
  private readonly results = signal<readonly ArtistSummary[]>([]);
  protected readonly liveMessage = signal('');

  protected readonly full = computed(
    () => this.credits().length >= EXHIBITION_LIMITS.artistsMaxCount,
  );

  /** Matches that can be added: not archived, and not credited already. */
  protected readonly offers = computed(() => {
    const credited = new Set(this.credits().map((artist) => artist.id));
    return this.results().filter(
      (artist) => artist.status !== 'Archived' && !credited.has(artist.id),
    );
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    if (value.trim() === '') {
      this.generation++;
      this.query.set('');
      this.state.set('idle');
      return;
    }
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
      void this.runSearch();
    }, SEARCH_DELAY_MS);
  }

  protected async runSearch(): Promise<void> {
    const search = this.query();
    if (search === '') return;
    const generation = ++this.generation;
    this.state.set('searching');
    try {
      const page = await firstValueFrom(this.artists.list({ search, limit: RESULT_LIMIT }));
      if (generation !== this.generation) return;
      this.results.set(page.items);
      this.state.set('ready');
      const count = this.offers().length;
      this.liveMessage.set(
        count === 0
          ? 'No artists to add match.'
          : `${count} ${count === 1 ? 'artist' : 'artists'} to add.`,
      );
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  protected add(artist: ArtistSummary): void {
    if (this.full() || this.credits().some((credit) => credit.id === artist.id)) return;
    this.credits.update((credits) => [
      ...credits,
      { id: artist.id, name: artist.name, slug: artist.slug, status: artist.status },
    ]);
    const count = this.credits().length;
    this.liveMessage.set(`${artist.name} added, ${ordinal(count)} of ${count}.`);
    // The button pressed has gone with its offer; the search is where adding continues.
    this.focusAfterRender(() =>
      this.host.nativeElement.querySelector<HTMLElement>('[name="artistSearch"]'),
    );
  }

  protected move(index: number, by: -1 | 1): void {
    const to = index + by;
    const credits = [...this.credits()];
    if (to < 0 || to >= credits.length) return;
    const [artist] = credits.splice(index, 1);
    credits.splice(to, 0, artist);
    this.credits.set(credits);
    this.liveMessage.set(`${artist.name} moved to ${ordinal(to + 1)} of ${credits.length}.`);
    // Focus follows the artist; at the top or bottom its button is disabled, so the other direction takes it.
    const action =
      (by === -1 && to === 0) || (by === 1 && to === credits.length - 1)
        ? by === -1
          ? 'down'
          : 'up'
        : by === -1
          ? 'up'
          : 'down';
    this.focusAfterRender(() =>
      this.host.nativeElement.querySelector<HTMLElement>(
        `[data-credit="${CSS.escape(artist.id)}"] [data-action="${action}"]`,
      ),
    );
  }

  protected remove(index: number): void {
    const credits = [...this.credits()];
    const [artist] = credits.splice(index, 1);
    this.credits.set(credits);
    this.liveMessage.set(`${artist.name} removed.`);
    // Focus goes to the artist that took its place, or the one before, or the search when none is left.
    const neighbour = credits[index] ?? credits[index - 1];
    this.focusAfterRender(() =>
      neighbour
        ? this.host.nativeElement.querySelector<HTMLElement>(
            `[data-credit="${CSS.escape(neighbour.id)}"] [data-action="remove"]`,
          )
        : this.host.nativeElement.querySelector<HTMLElement>('[name="artistSearch"]'),
    );
  }

  private focusAfterRender(target: () => HTMLElement | null): void {
    afterNextRender(() => target()?.focus(), { injector: this.injector });
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}

function ordinal(position: number): string {
  const tens = position % 100;
  const suffix =
    tens >= 11 && tens <= 13
      ? 'th'
      : (({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[position % 10] ?? 'th');
  return `${position}${suffix}`;
}
