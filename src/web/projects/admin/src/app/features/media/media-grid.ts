import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChildren,
} from '@angular/core';
import { UiIcon, UiStatusChip } from '@6311/ui';
import { MediaAssetSummary, formatBytes } from '../../models/media';

/**
 * The library's tiles. Presentational: the library opens the editor from it, and the picker selects from it.
 * Each tile is a button whose name is its visible text (file name, size, status), so the thumbnail itself is
 * decorative — a person using a screen reader hears what a sighted person reads beside it.
 */
@Component({
  selector: 'cms-media-grid',
  imports: [UiIcon, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- role="list": Safari drops list semantics from a list styled without bullets. -->
    <ul class="grid" role="list" [attr.aria-label]="label()">
      @for (item of items(); track item.id) {
        <li>
          <button
            #tile
            type="button"
            class="tile"
            [class.tile--selected]="selectable() && item.id === selectedId()"
            [attr.aria-pressed]="selectable() ? item.id === selectedId() : null"
            (click)="chosen.emit(item)"
          >
            <span class="tile__frame">
              @if (item.thumbnailUrl; as url) {
                <img class="tile__img" [src]="url" alt="" loading="lazy" decoding="async" />
              } @else {
                <ui-icon class="tile__placeholder" name="frame" [size]="40" [stroke]="1.2" />
              }
            </span>
            <span class="tile__name">{{ item.fileName || 'Untitled image' }}</span>
            <span class="tile__meta"
              >{{ item.width }}×{{ item.height }} · {{ size(item.bytes) }}</span
            >
            <span class="tile__chips">
              @switch (item.processingStatus) {
                @case ('Pending') {
                  <ui-status-chip tone="pending" icon="clock">Processing</ui-status-chip>
                }
                @case ('Failed') {
                  <ui-status-chip tone="attention" icon="alert">Failed</ui-status-chip>
                }
              }
              @if (!item.altText) {
                <ui-status-chip tone="attention" icon="info">Needs alt text</ui-status-chip>
              }
              @if (item.visibility === 'Published') {
                <ui-status-chip tone="positive" icon="check">Published</ui-status-chip>
              }
            </span>
          </button>
        </li>
      }
    </ul>
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 180px), 1fr));
      gap: var(--space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .tile {
      display: grid;
      align-content: start;
      gap: var(--space-1);
      width: 100%;
      height: 100%;
      padding: var(--space-2);
      border: var(--border-thin) solid var(--line);
      background: var(--white);
      color: var(--ink);
      font: inherit;
      text-align: left;
      cursor: pointer;
      transition: border-color var(--dur-fast) var(--ease);
    }
    .tile:hover {
      border-color: var(--ink);
    }
    .tile:focus-visible {
      outline: var(--border) solid var(--focus);
      outline-offset: var(--focus-offset);
    }
    .tile--selected {
      border-color: var(--ink);
      box-shadow: inset 0 0 0 var(--border) var(--ink);
    }
    .tile__frame {
      display: grid;
      place-items: center;
      aspect-ratio: 4 / 3;
      overflow: hidden;
      background: var(--light);
    }
    .tile__img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .tile__placeholder {
      color: var(--muted);
    }
    /* Two lines, wrapping anywhere, so a long name stays readable at 200% and on a phone. */
    .tile__name {
      display: -webkit-box;
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
      line-clamp: 2;
      overflow: hidden;
      overflow-wrap: anywhere;
      font-size: var(--fs-meta);
      font-weight: var(--fw-bold);
    }
    .tile__meta {
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .tile__chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    @media (prefers-reduced-motion: reduce) {
      .tile {
        transition: none;
      }
    }
  `,
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
