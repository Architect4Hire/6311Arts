import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChildren,
} from '@angular/core';
import { UiIcon, UiStatusChip } from '@6311/ui';
import { MediaAssetSummary, formatBytes } from '../../../models/media';

/**
 * The library's tiles. Presentational: the library opens the editor from it, and the picker selects from it.
 * Each tile is a button whose name is its visible text (file name, size, status), so the thumbnail itself is
 * decorative — a person using a screen reader hears what a sighted person reads beside it.
 */
@Component({
  selector: 'cms-media-grid',
  imports: [UiIcon, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './media-grid.html',
  styleUrl: './media-grid.scss',
})
export class MediaGrid {
  readonly items = input.required<readonly MediaAssetSummary[]>();
  /** The list's accessible name, for example "Images in the library". */
  readonly label = input.required<string>();
  /** Picker mode: tiles are toggle buttons and one can be selected. */
  readonly selectable = input(false);
  readonly selectedId = input<string | null>(null);
  readonly chosen = output<MediaAssetSummary>();

  private readonly tiles = viewChildren<ElementRef<HTMLButtonElement>>('tile');

  protected readonly size = formatBytes;

  /** Moves focus to a tile, for example the first of a page just loaded. */
  focusTile(index: number): void {
    this.tiles()[index]?.nativeElement.focus();
  }
}
