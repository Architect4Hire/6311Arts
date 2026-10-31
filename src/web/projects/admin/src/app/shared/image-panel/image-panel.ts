import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiNotice } from '@6311/ui';
import { MediaAsset, MediaAssetSummary } from '../../models/media';
import { MediaService } from '../../services/media.service';
import { MediaPicker } from '../../features/media/media-picker/media-picker';

/** What the panel shows of the image: a thumbnail and whether it has the alt text publishing needs. */
interface Shown {
  readonly id: string;
  readonly url: string | null;
  readonly altText: string | null;
  /** The image's details couldn't be fetched: its alt text is unknown, not missing. */
  readonly unavailable?: boolean;
}

let nextId = 0;

/**
 * One image a record names from the media library — an artist's portrait, an exhibition's hero — with its
 * thumbnail, the alt-text warning publishing would refuse on, and Choose / Change / Remove through the media picker.
 *
 * ```html
 * <cms-image-panel heading="Portrait" noun="artist" [(mediaId)]="…" [error]="errors().portraitMediaId ?? null" />
 * ```
 */
@Component({
  selector: 'cms-image-panel',
  imports: [MediaPicker, UiButton, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './image-panel.html',
  styleUrl: './image-panel.scss',
})
export class ImagePanel {
  /** "Portrait", "Hero image". */
  readonly heading = input.required<string>();
  /** The record the image belongs to, as the alt-text warning names it: "artist". */
  readonly noun = input.required<string>();
  /** Cropped to 3:2 (a portrait), or at its own height with a rule round it. */
  readonly shape = input<'crop' | 'natural'>('crop');
  readonly mediaId = model<string | null>(null);
  readonly error = input<string | null>(null);
  readonly disabled = input(false);

  private readonly media = inject(MediaService);
  private readonly button = viewChild.required<ElementRef<HTMLElement>>('chooseButton');

  protected readonly headingId = `cms-image-panel-${nextId++}`;
  protected readonly word = computed(() => this.heading().toLowerCase());
  protected readonly shown = signal<Shown | null>(null);
  protected readonly picking = signal(false);

  constructor() {
    // The thumbnail and alt text follow whichever image the record names.
    effect(() => {
      const id = this.mediaId();
      untracked(() => void this.show(id));
    });
  }

  /** What an error about this image sends focus to: the button that fixes it. */
  focusTarget(): HTMLElement {
    return this.button().nativeElement;
  }

  protected choose(choice: MediaAssetSummary): void {
    this.shown.set({ id: choice.id, url: choice.thumbnailUrl, altText: choice.altText });
    this.mediaId.set(choice.id);
  }

  protected remove(): void {
    this.mediaId.set(null);
  }

  protected retry(): void {
    void this.show(this.mediaId(), true);
  }

  private async show(id: string | null, retry = false): Promise<void> {
    if (id === null) {
      this.shown.set(null);
      return;
    }
    if (!retry && this.shown()?.id === id) return;

    this.shown.set(null);
    try {
      const asset = await firstValueFrom(this.media.get(id));
      if (this.mediaId() === id) this.shown.set(shownOf(asset));
    } catch {
      if (this.mediaId() === id)
        this.shown.set({ id, url: null, altText: null, unavailable: true });
    }
  }
}

function shownOf(asset: MediaAsset): Shown {
  const url =
    asset.renditions.find((rendition) => rendition.name === '800.webp')?.url ??
    asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ??
    [...asset.renditions].sort((a, b) => a.width - b.width)[0]?.url ??
    null;
  return { id: asset.id, url, altText: asset.altText };
}
