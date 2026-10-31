import { ChangeDetectionStrategy, Component, inject, input, model } from '@angular/core';
import { ArtistService } from '../../../services/artist.service';
import { PickedRecord, PickerWords, SearchPicker } from '../search-picker/search-picker';

/** The artist a record credits, as little of them as a picker needs to show. */
export type PickedArtist = PickedRecord;

const WORDS: PickerWords = {
  noun: 'artist',
  plural: 'artists',
  verb: 'Credit',
  done: 'credited',
  where: 'the roster',
  none: 'Nobody credited yet.',
  cleared: 'Credit removed.',
  offered: 'to credit',
  noMatch: 'No artist on the roster matches',
  field: 'artistSearch',
};

/**
 * The one artist a record credits (`<cms-artist-picker [(artist)]>`): a `<cms-search-picker>` over the roster.
 * For a record that credits several artists in an order, Exhibitions has `<cms-artist-credits>` instead.
 */
@Component({
  selector: 'cms-artist-picker',
  imports: [SearchPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './artist-picker.html',
})
export class ArtistPicker {
  /** The credited artist, or `null`. Two-way: every change is the editor's draft. */
  readonly artist = model.required<PickedArtist | null>();
  /** What the panel is called, so a screen can say "Artist" or "Also an artist". */
  readonly heading = input('Artist');
  readonly disabled = input(false);
  readonly error = input<string | null>(null);
  readonly searchMaxLength = input(100);

  private readonly artists = inject(ArtistService);

  protected readonly words = WORDS;
  protected readonly search = (search: string, limit: number) =>
    this.artists.list({ search, limit });
}
