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
import { ArtistSummary } from '../../../models/artist';
import { STATUS_CHIPS } from '../../../models/content';
import { ArtistService } from '../../../services/artist.service';

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
  templateUrl: './artist-picker.html',
  styleUrl: './artist-picker.scss',
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
        this.host.nativeElement.querySelector<HTMLElement>('input[name="artistSearch"]')?.focus(),
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
        count === 0
          ? 'No artists match.'
          : `${count} ${count === 1 ? 'artist' : 'artists'} to credit.`,
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
