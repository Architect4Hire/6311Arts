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
import { ArtistSummary } from '../models/artist';
import { STATUS_CHIPS } from '../models/content';
import { ArtistService } from '../services/artist.service';

/** How long typing pauses before the roster is searched. */
const SEARCH_DELAY_MS = 300;

/** How many matches to offer at once: enough to find a name, few enough to read. */
const RESULT_LIMIT = 8;

/** The artist a record credits, as little of them as a picker needs to show. */
export interface PickedArtist {
  readonly id: string;
  readonly name: string;
  readonly status: ArtistSummary['status'];
}

type SearchState = 'idle' | 'searching' | 'failed' | 'ready';

/**
 * The one artist a record credits (`<cms-artist-picker [(artist)]>`): who it is now, a search of the roster to
 * change it, and a way to take the credit off. Archived artists are not offered — archive is how an artist is
 * retired, and a retired artist gains no new work (ADR 0033).
 *
 * For a record that credits several artists in an order, Exhibitions has `<cms-artist-credits>` instead.
 */
@Component({
  selector: 'cms-artist-picker',
  imports: [UiButton, UiControl, UiField, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="artist-heading">
      <h2 id="artist-heading" class="panel__title">{{ heading() }}</h2>

      <!-- Exists from the start, so what is written into it later is read out. -->
      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

      @if (artist(); as credited) {
        <p class="credited">
          <span class="credited__name">{{ credited.name }}</span>
          @if (credited.status !== 'Published') {
            <ui-status-chip [tone]="chips[credited.status].tone" [icon]="chips[credited.status].icon">{{
              chips[credited.status].label
            }}</ui-status-chip>
          }
        </p>
        <div class="actions">
          <button
            uiButton
            type="button"
            size="sm"
            data-action="remove-artist"
            [disabled]="disabled()"
            [attr.aria-label]="'Remove ' + credited.name"
            (click)="clear()"
          >
            Remove
          </button>
        </div>
      } @else {
        <p class="panel__empty">Nobody credited yet.</p>
      }

      <ui-field
        [label]="artist() ? 'Credit someone else' : 'Credit an artist'"
        hint="Search the roster by name. Archived artists can’t be credited."
        [error]="error()"
      >
        <input
          #searchInput
          uiControl
          type="search"
          name="artistSearch"
          autocomplete="off"
          [attr.maxlength]="searchMaxLength()"
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
            <p class="panel__hint">No artist on the roster matches “{{ query() }}”.</p>
          } @else {
            <ul class="offers" aria-label="Matching artists">
              @for (candidate of offers(); track candidate.id) {
                <li class="offer">
                  <button
                    uiButton
                    type="button"
                    size="sm"
                    [disabled]="disabled()"
                    [attr.aria-label]="'Credit ' + candidate.name"
                    (click)="choose(candidate)"
                  >
                    Credit
                  </button>
                  <span>
                    {{ candidate.name }}
                    @if (candidate.medium) {
                      <span class="offer__medium">{{ candidate.medium }}</span>
                    }
                  </span>
                </li>
              }
            </ul>
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
    .credited {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      margin: 0;
    }
    .credited__name {
      font-weight: var(--fw-bold);
      overflow-wrap: anywhere;
    }
    .offers {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
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
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
  `,
})
export class ArtistPicker {
  /** The credited artist, or `null`. Two-way: every change is the editor's draft. */
  readonly artist = model.required<PickedArtist | null>();

  /** What the panel is called, so a screen can say "Artist" or "Instructor". */
  readonly heading = input('Artist');

  readonly disabled = input(false);

  /** The API's or the form's word on the credit, shown above the search. */
  readonly error = input<string | null>(null);

  readonly searchMaxLength = input(100);

  private readonly artists = inject(ArtistService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bumped by every search, so an answer to an older one is dropped. */
  private generation = 0;

  protected readonly chips = STATUS_CHIPS;
  protected readonly search = signal('');
  /** The search as last sent. */
  protected readonly query = signal('');
  protected readonly state = signal<SearchState>('idle');
  private readonly results = signal<readonly ArtistSummary[]>([]);
  protected readonly liveMessage = signal('');

  /** Matches that can be credited: not archived, and not the one credited already. */
  protected readonly offers = computed(() => {
    const credited = this.artist()?.id;
    return this.results().filter(
      (candidate) => candidate.status !== 'Archived' && candidate.id !== credited,
    );
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
      void this.runSearch();
    }, SEARCH_DELAY_MS);
  }

  protected choose(candidate: ArtistSummary): void {
    this.artist.set({ id: candidate.id, name: candidate.name, status: candidate.status });
    this.announce(`${candidate.name} credited.`);

    // The button that was clicked is gone — a credited artist is no longer offered — so focus returns to the search.
    this.focusSearch();
  }

  protected clear(): void {
    const name = this.artist()?.name;
    this.artist.set(null);
    this.announce(name ? `${name} removed.` : 'Credit removed.');

    // The Remove button went with the credit it removed.
    this.focusSearch();
  }

  private focusSearch(): void {
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>('input[name="artistSearch"]')
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected async runSearch(): Promise<void> {
    const search = this.query();
    if (search === '') {
      this.state.set('idle');
      return;
    }

    const generation = ++this.generation;
    this.state.set('searching');
    try {
      const page = await firstValueFrom(this.artists.list({ search, limit: RESULT_LIMIT }));
      if (generation !== this.generation) return;
      this.results.set(page.items);
      this.state.set('ready');
      const count = this.offers().length;
      this.announce(
        count === 0 ? 'No artists match.' : `${count} ${count === 1 ? 'artist' : 'artists'} to credit.`,
      );
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
