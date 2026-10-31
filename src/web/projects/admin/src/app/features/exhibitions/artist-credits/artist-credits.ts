import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import { UiButton, UiStatusChip } from '@6311/ui';
import { STATUS_CHIPS } from '../../../models/content';
import { CreditedArtist, EXHIBITION_LIMITS } from '../../../models/exhibition';
import { ArtistService } from '../../../services/artist.service';
import {
  PickerCandidate,
  RecordSearch,
  RecordSearchFn,
  SearchWords,
} from '../../../shared/pickers/record-search/record-search';

const WORDS: SearchWords = {
  noun: 'artist',
  plural: 'artists',
  verb: 'Add',
  done: 'credited',
  where: 'the roster',
  offered: 'to add',
  noMatch: 'No artist on the roster to add matches',
  field: 'artistSearch',
};

/**
 * An exhibition's credits (`<cms-artist-credits [(credits)]>`): the artists it shows, in the order the site lists
 * them, and a `<cms-record-search>` of the roster to add more. Archived artists and those already credited are not
 * offered.
 *
 * Ordering is by "Move up" / "Move down" buttons rather than drag and drop, so it works the same by keyboard, by
 * screen reader and by touch. Each button names its artist, and focus stays on the artist that moved.
 */
@Component({
  selector: 'cms-artist-credits',
  imports: [RecordSearch, UiButton, UiStatusChip],
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
  private readonly finder = viewChild(RecordSearch);

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly words = WORDS;
  protected readonly search: RecordSearchFn = (search, limit) =>
    this.artists.list({ search, limit });
  protected readonly liveMessage = signal('');

  protected readonly full = computed(
    () => this.credits().length >= EXHIBITION_LIMITS.artistsMaxCount,
  );

  /** The artists credited already, which are not offered again. */
  protected readonly credited = computed(() => this.credits().map((artist) => artist.id));

  protected add(artist: PickerCandidate): void {
    if (this.full() || this.credits().some((credit) => credit.id === artist.id)) return;
    this.credits.update((credits) => [
      ...credits,
      { id: artist.id, name: artist.name, slug: artist.slug ?? '', status: artist.status },
    ]);
    const count = this.credits().length;
    this.liveMessage.set(`${artist.name} added, ${ordinal(count)} of ${count}.`);
    // The button pressed has gone with its offer; the search is where adding continues.
    this.focusAfterRender(() => this.finder());
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
        : this.finder(),
    );
  }

  private focusAfterRender(target: () => { focus(): void } | null | undefined): void {
    afterNextRender(() => target()?.focus(), { injector: this.injector });
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
