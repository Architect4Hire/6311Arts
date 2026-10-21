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
import { CreditedArtist, EXHIBITION_LIMITS } from '../../../models/exhibition';
import { ArtistService } from '../../../services/artist.service';

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
  templateUrl: './artist-credits.html',
  styleUrl: './artist-credits.scss',
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
