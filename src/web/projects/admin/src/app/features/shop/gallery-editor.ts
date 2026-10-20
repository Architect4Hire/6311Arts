import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiNotice } from '@6311/ui';
import { MediaAsset, MediaAssetSummary } from '../../models/media';
import { SHOP_LIMITS } from '../../models/shop';
import { MediaService } from '../../services/media.service';
import { MediaPicker } from '../media/media-picker';

/** What the editor shows of one photograph: a thumbnail, and whether it has the alt text publishing needs. */
interface Photograph {
  readonly id: string;
  readonly url: string | null;
  readonly altText: string | null;
  /** The image's details couldn't be fetched: its alt text is unknown, not missing. */
  readonly unavailable?: boolean;
}

let nextErrorId = 0;

/**
 * A piece's photographs (`<cms-gallery-editor [(images)]>`): the pictures the product page shows, in the order
 * someone chose, with the first as the lead (ADR 0038).
 *
 * Ordering is by "Move up" / "Move down" rather than drag and drop, so it works the same by keyboard, by screen
 * reader and by touch — as the exhibition credits do. Each button names its photograph's position, and focus stays
 * with the picture that moved.
 */
@Component({
  selector: 'cms-gallery-editor',
  imports: [MediaPicker, UiButton, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="gallery-heading">
      <h2 id="gallery-heading" class="panel__title">Photographs</h2>

      <!-- Exists from the start, so what is written into it later is read out. -->
      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

      @if (images().length === 0) {
        <p class="panel__empty">
          No photographs yet. A piece needs at least one, described, before it can be published.
        </p>
      } @else {
        <ol class="gallery" aria-label="Photographs, in order">
          @for (id of images(); track id; let index = $index, first = $first, last = $last) {
            <li class="shot" [attr.data-shot]="index">
              @if (shot(id); as photograph) {
                @if (photograph.unavailable) {
                  <div class="shot__thumb shot__thumb--missing">Not loaded</div>
                } @else if (photograph.url) {
                  <img class="shot__thumb" [src]="photograph.url" [alt]="photograph.altText ?? ''" />
                } @else {
                  <div class="shot__thumb shot__thumb--pending">No preview yet</div>
                }
              } @else {
                <div class="shot__thumb shot__thumb--pending">Loading…</div>
              }

              <div class="shot__detail">
                @if (first) {
                  <p class="shot__lead">Lead photograph</p>
                }
                @if (shot(id); as photograph) {
                  @if (!photograph.unavailable && !photograph.altText) {
                    <ui-notice tone="warning">
                      No alt text yet. Add it in Media before publishing.
                    </ui-notice>
                  }
                }
              </div>

              <div class="shot__actions">
                <button
                  uiButton
                  type="button"
                  size="sm"
                  data-action="up"
                  [disabled]="disabled() || first"
                  [attr.aria-label]="'Move photograph ' + (index + 1) + ' up'"
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
                  [attr.aria-label]="'Move photograph ' + (index + 1) + ' down'"
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
                  [attr.aria-label]="'Remove photograph ' + (index + 1)"
                  (click)="remove(index)"
                >
                  Remove
                </button>
              </div>
            </li>
          }
        </ol>
      }

      @if (error(); as message) {
        <p class="error" role="alert" [id]="errorId">{{ message }}</p>
      }

      @if (full()) {
        <p class="panel__hint">
          A piece has at most {{ limits.galleryMaxCount }} photographs. Remove one to add another.
        </p>
      } @else {
        <div class="actions">
          <button
            uiButton
            type="button"
            size="sm"
            data-action="add"
            [disabled]="disabled()"
            [attr.aria-describedby]="error() ? errorId : null"
            (click)="picking.set(true)"
          >
            Add photograph
          </button>
        </div>
      }

      <cms-media-picker [(open)]="picking" (picked)="add($event)" />
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
    .gallery {
      display: grid;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .shot {
      display: grid;
      grid-template-columns: 72px minmax(0, 1fr);
      gap: var(--space-2) var(--space-3);
      align-items: start;
      padding-bottom: var(--space-3);
      border-bottom: var(--border-thin) solid var(--line);
    }
    .shot__thumb {
      display: block;
      width: 72px;
      height: 72px;
      object-fit: cover;
      border: var(--border-thin) solid var(--line);
    }
    .shot__thumb--pending,
    .shot__thumb--missing {
      display: grid;
      place-items: center;
      padding: var(--space-1);
      font-size: var(--fs-small);
      color: var(--text-soft);
      text-align: center;
    }
    .shot__detail {
      display: grid;
      gap: var(--space-2);
      min-width: 0;
    }
    .shot__lead {
      margin: 0;
      font-size: var(--fs-small);
      font-weight: var(--fw-bold);
    }
    .shot__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      grid-column: 2;
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
export class GalleryEditor {
  /** The photographs, in order. Two-way: every change is the editor's draft. */
  readonly images = model.required<readonly string[]>();

  readonly disabled = input(false);

  /** The API's or the form's word on the gallery, shown under it. */
  readonly error = input<string | null>(null);

  private readonly media = inject(MediaService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** What Media said about each photograph, by id. Filled in as the answers arrive. */
  private readonly described = signal<ReadonlyMap<string, Photograph>>(new Map());

  /** The pictures already being asked about, so a quick reorder does not ask twice. */
  private readonly inFlight = new Set<string>();

  protected readonly limits = SHOP_LIMITS;
  protected readonly picking = signal(false);
  protected readonly liveMessage = signal('');
  /** The id the controls point at when there is a refusal to read. */
  protected readonly errorId = `gallery-error-${++nextErrorId}`;

  protected readonly full = computed(() => this.images().length >= SHOP_LIMITS.galleryMaxCount);

  constructor() {
    // Each photograph's thumbnail and alt text follow whichever images the draft names.
    effect(() => {
      const wanted = this.images();
      untracked(() => void this.describe(wanted));
    });
  }

  protected shot(id: string): Photograph | undefined {
    return this.described().get(id);
  }

  protected add(choice: MediaAssetSummary): void {
    if (this.images().includes(choice.id)) {
      this.announce('That photograph is already in the gallery.');
      return;
    }

    this.remember({ id: choice.id, url: choice.thumbnailUrl, altText: choice.altText });
    this.images.update((images) => [...images, choice.id]);
    this.announce(`Photograph ${this.images().length} added.`);
  }

  protected move(index: number, by: number): void {
    const images = [...this.images()];
    const target = index + by;
    if (target < 0 || target >= images.length) return;

    [images[index], images[target]] = [images[target], images[index]];
    this.images.set(images);
    this.announce(
      target === 0 ? 'Photograph moved to the front: it is now the lead.' : `Photograph moved to ${target + 1}.`,
    );

    // Focus stays with the picture that moved, wherever it landed.
    afterNextRender(
      () => {
        const row = this.host.nativeElement.querySelector<HTMLElement>(`[data-shot="${target}"]`);

        // The button it was moved with, if it still works; at either end that one is disabled, so the one for the
        // opposite move takes the focus, as the exhibition credits do.
        const moved = by < 0 ? 'up' : 'down';
        const back = by < 0 ? 'down' : 'up';
        const button =
          row?.querySelector<HTMLButtonElement>(`[data-action="${moved}"]:not([disabled])`) ??
          row?.querySelector<HTMLButtonElement>(`[data-action="${back}"]:not([disabled])`) ??
          row?.querySelector<HTMLButtonElement>('[data-action="remove"]');
        button?.focus();
      },
      { injector: this.injector },
    );
  }

  protected remove(index: number): void {
    this.images.update((images) => images.filter((_, position) => position !== index));
    this.announce(
      index === 0 && this.images().length > 0
        ? 'Photograph 1 removed. The next one now leads.'
        : `Photograph ${index + 1} removed.`,
    );

    // The button that was clicked is gone with its row, so focus moves to the one that took its place — or to
    // "Add photograph" when the last one went.
    afterNextRender(
      () => {
        const remaining = this.images().length;
        const target = Math.min(index, remaining - 1);
        const next = this.host.nativeElement.querySelector<HTMLElement>(
          remaining === 0 ? '[data-action="add"]' : `[data-shot="${target}"] [data-action="remove"]`,
        );
        next?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Asks Media about any photograph not described yet, and forgets the ones no longer in the gallery. */
  private async describe(wanted: readonly string[]): Promise<void> {
    const keep = new Map([...this.described()].filter(([id]) => wanted.includes(id)));
    this.described.set(keep);

    // Asking twice for one picture is only waste, but remembering one that has since been removed would show it
    // again, so both are kept out by the in-flight set and the check after the answer.
    const asking = wanted.filter((candidate) => !keep.has(candidate) && !this.inFlight.has(candidate));
    for (const id of asking) {
      this.inFlight.add(id);
    }

    await Promise.all(
      asking.map(async (id) => {
        try {
          const asset = await firstValueFrom(this.media.get(id));
          if (this.images().includes(id)) this.remember(photographOf(asset));
        } catch {
          if (this.images().includes(id)) {
            this.remember({ id, url: null, altText: null, unavailable: true });
          }
        } finally {
          this.inFlight.delete(id);
        }
      }),
    );
  }

  private remember(photograph: Photograph): void {
    this.described.update((described) => new Map(described).set(photograph.id, photograph));
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }
}

function photographOf(asset: MediaAsset): Photograph {
  const url =
    asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ??
    asset.renditions.find((rendition) => rendition.name === '800.webp')?.url ??
    [...asset.renditions].sort((a, b) => a.width - b.width)[0]?.url ??
    null;
  return { id: asset.id, url, altText: asset.altText };
}
