import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiDialog, UiEmptyState, UiNotice } from '@6311/ui';
import { MediaAssetSummary, MediaPage } from '../../../models/media';
import { MediaService } from '../../../services/media.service';
import { MediaGrid } from '../media-grid/media-grid';

/**
 * Chooses one image from the library, for any editor that needs one (an artist's portrait, an exhibition's hero).
 * Open it with `[(open)]`; it emits the chosen tile and closes.
 *
 * ```html
 * <cms-media-picker [(open)]="picking" (picked)="portrait.set($event)" />
 * ```
 *
 * An image without alt text can be chosen — the editor may add it next — but says plainly that it can't go on
 * published content until it has some (media.md).
 */
@Component({
  selector: 'cms-media-picker',
  imports: [MediaGrid, UiButton, UiDialog, UiEmptyState, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './media-picker.html',
  styleUrl: './media-picker.scss',
})
export class MediaPicker {
  readonly open = model(false);
  readonly picked = output<MediaAssetSummary>();

  private readonly media = inject(MediaService);
  private generation = 0;

  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly MediaAssetSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly selected = signal<MediaAssetSummary | null>(null);
  protected readonly missingAltText = computed(() => {
    const choice = this.selected();
    return choice !== null && !choice.altText;
  });

  constructor() {
    // Loaded fresh each time it opens, so an image uploaded a moment ago is there.
    effect(() => {
      if (this.open()) untracked(() => void this.reload());
    });
  }

  protected async reload(): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.selected.set(null);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.media.list());
      if (generation !== this.generation) return;
      this.apply(page, false);
      this.state.set('ready');
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  protected async more(): Promise<void> {
    const cursor = this.next();
    if (!cursor || this.loadingMore()) return;
    const generation = this.generation;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.media.list({ cursor }));
      if (generation === this.generation) this.apply(page, true);
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected pick(): void {
    const choice = this.selected();
    if (!choice) return;
    this.picked.emit(choice);
    this.open.set(false);
  }

  private apply(page: MediaPage, append: boolean): void {
    this.items.update((items) => (append ? [...items, ...page.items] : page.items));
    this.next.set(page.nextCursor);
  }
}
