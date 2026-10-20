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
import { MediaAssetSummary, MediaPage } from '../../models/media';
import { MediaService } from '../../services/media.service';
import { MediaGrid } from './media-grid';

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
  template: `
    <ui-dialog [(open)]="open" heading="Choose an image">
      @switch (state()) {
        @case ('loading') {
          <p role="status">Loading the library…</p>
        }
        @case ('failed') {
          <ui-notice tone="error" heading="The library couldn’t be loaded">
            <button uiButton type="button" size="sm" (click)="reload()">Try again</button>
          </ui-notice>
        }
        @default {
          @if (items().length === 0) {
            <ui-empty-state icon="frame" heading="No images yet" [level]="3">
              <p>Upload images in Media, then choose one here.</p>
            </ui-empty-state>
          } @else {
            <cms-media-grid
              label="Images to choose from"
              [items]="items()"
              [selectable]="true"
              [selectedId]="selected()?.id ?? null"
              (chosen)="selected.set($event)"
            />
            @if (moreFailed()) {
              <ui-notice tone="error"
                >More images couldn’t be loaded. The ones above are still here.</ui-notice
              >
            }
            @if (next()) {
              <p>
                <button uiButton type="button" size="sm" [busy]="loadingMore()" (click)="more()">
                  {{ moreFailed() ? 'Try loading more again' : 'Load more' }}
                </button>
              </p>
            }
          }
        }
      }

      <!-- Exists from the start, so the warning that appears on selection is read out. -->
      <div aria-live="polite">
        @if (missingAltText()) {
          <ui-notice tone="warning">
            This image has no alt text yet. It can be chosen, but it can’t go on published content
            until it has some.
          </ui-notice>
        }
      </div>

      <div uiDialogActions>
        <button uiButton type="button" (click)="open.set(false)">Cancel</button>
        <button
          uiButton
          type="button"
          variant="dark"
          aria-describedby="media-picker-hint"
          [disabled]="!selected()"
          (click)="pick()"
        >
          Use this image
        </button>
        @if (!selected()) {
          <span id="media-picker-hint" class="hint">Choose an image first.</span>
        }
      </div>
    </ui-dialog>
  `,
  styles: `
    .hint {
      align-self: center;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
  `,
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
