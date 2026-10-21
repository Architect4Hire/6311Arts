import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiDialog, UiEmptyState, UiNotice } from '@6311/ui';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import { MediaAsset, MediaAssetSummary, MediaListQuery, MediaPage } from '../../../models/media';
import { MediaService } from '../../../services/media.service';
import { MediaEditor } from '../media-editor/media-editor';
import { MediaGrid } from '../media-grid/media-grid';
import { MediaUploader } from '../media-uploader/media-uploader';

type Filter = 'all' | 'missing-alt' | 'processing' | 'failed';

const FILTERS: readonly { readonly value: Filter; readonly label: string }[] = [
  { value: 'all', label: 'All images' },
  { value: 'missing-alt', label: 'Needs alt text' },
  { value: 'processing', label: 'Processing' },
  { value: 'failed', label: 'Failed' },
];

/** How often tiles still being processed are asked about again. */
const POLL_MS = 3000;

/**
 * The media library (`/admin/media`): upload, browse, and open an image to edit its alt text, credit and
 * compression. New uploads appear at the top straight away and update themselves as the Worker finishes their
 * website sizes.
 */
@Component({
  selector: 'cms-media-library',
  imports: [MediaEditor, MediaGrid, MediaUploader, UiButton, UiDialog, UiEmptyState, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './media-library.html',
  styleUrl: './media-library.scss',
})
export class MediaLibrary implements HasUnsavedChanges {
  private readonly media = inject(MediaService);
  private readonly confirm = inject(ConfirmService);
  private readonly injector = inject(Injector);
  private readonly editor = viewChild(MediaEditor);
  private readonly uploader = viewChild(MediaUploader);
  private readonly grid = viewChild(MediaGrid);
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');
  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every new list request, so an answer to an older one (a filter since changed) is dropped. */
  private generation = 0;

  protected readonly filters = FILTERS;
  protected readonly filter = signal<Filter>('all');
  protected readonly state = signal<'loading' | 'failed' | 'ready'>('loading');
  protected readonly items = signal<readonly MediaAssetSummary[]>([]);
  protected readonly next = signal<string | null>(null);
  protected readonly loadingMore = signal(false);
  protected readonly moreFailed = signal(false);
  protected readonly editing = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly liveMessage = signal('');

  protected readonly emptyHeading = computed(() =>
    this.filter() === 'all' ? 'No images yet' : 'Nothing to show here',
  );
  protected readonly emptyText = computed(() => {
    switch (this.filter()) {
      case 'missing-alt':
        return 'Every image has alt text.';
      case 'processing':
        return 'No image is waiting for its website sizes.';
      case 'failed':
        return 'No image has failed.';
      default:
        return 'Drop images on the uploader or choose files. They’re ready for content once their website sizes are made.';
    }
  });

  private readonly pending = computed(() =>
    this.items()
      .filter((item) => item.processingStatus === 'Pending')
      .map((item) => item.id),
  );

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.filter();
      untracked(() => void this.reload());
    });

    // Poll only while something is processing, and stop with the screen.
    effect(() => {
      const waiting = this.pending().length > 0;
      untracked(() => (waiting ? this.schedulePoll() : this.stopPolling()));
    });
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  hasUnsavedChanges(): boolean {
    return (this.editor()?.dirty() ?? false) || (this.uploader()?.busy() ?? false);
  }

  unsavedChangesMessage(): string {
    return this.editor()?.dirty()
      ? 'Your changes to an image haven’t been saved. Leaving now loses them.'
      : 'Images are still uploading. Leaving now stops the uploads that haven’t finished.';
  }

  protected async reload(): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.media.list(this.query()));
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
    const firstNew = this.items().length;
    this.loadingMore.set(true);
    this.moreFailed.set(false);
    try {
      const page = await firstValueFrom(this.media.list({ ...this.query(), cursor }));
      if (generation !== this.generation) return;
      this.apply(page, true);
      this.liveMessage.set(`${page.items.length} more image(s) loaded.`);
      // The button may be gone (last page); focus moves to the first tile that just arrived.
      afterNextRender(() => this.grid()?.focusTile(firstNew), { injector: this.injector });
    } catch {
      if (generation === this.generation) this.moreFailed.set(true);
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected openEditor(id: string): void {
    this.editing.set(id);
    this.editorOpen.set(true);
  }

  /**
   * Every way out of the editor comes here — its Close button, the dialog's, Escape — so unsaved alt text is never
   * dropped without asking. Keeping it re-shows the same dialog; the editor and its draft were never destroyed.
   */
  protected async requestClose(): Promise<void> {
    if (this.editing() === null) return;
    if (this.editor()?.dirty()) {
      const discard = await this.confirm.confirm({
        heading: 'Discard unsaved changes?',
        message: 'Your changes to this image haven’t been saved. Closing now loses them.',
        confirmLabel: 'Discard changes',
        cancelLabel: 'Keep editing',
      });
      if (!discard) {
        this.editorOpen.set(true);
        return;
      }
    }
    this.closeEditor();
  }

  protected onUploaded(asset: MediaAsset): void {
    // Only into a list that is showing: added to a failed or loading one, it would make it look complete.
    if (this.state() !== 'ready') return;
    if (this.filter() !== 'all' && !matches(this.filter(), toSummary(asset))) return;
    this.items.update((items) => [
      toSummary(asset),
      ...items.filter((item) => item.id !== asset.id),
    ]);
  }

  protected onSaved(asset: MediaAsset): void {
    this.replace(asset);
  }

  protected onDeleted(id: string): void {
    this.items.update((items) => items.filter((item) => item.id !== id));
    this.closeEditor();
    this.liveMessage.set('Image deleted.');
    // The tile that opened the dialog is gone, so focus would fall to the page; it goes to the heading instead.
    afterNextRender(() => this.heading().nativeElement.focus(), { injector: this.injector });
  }

  private closeEditor(): void {
    this.editorOpen.set(false);
    this.editing.set(null);
  }

  private query(): MediaListQuery {
    switch (this.filter()) {
      case 'missing-alt':
        return { missingAltText: true };
      case 'processing':
        return { status: 'Pending' };
      case 'failed':
        return { status: 'Failed' };
      default:
        return {};
    }
  }

  private apply(page: MediaPage, append: boolean): void {
    this.items.update((items) => (append ? [...items, ...page.items] : page.items));
    this.next.set(page.nextCursor);
  }

  private replace(asset: MediaAsset): void {
    this.items.update((items) =>
      items.map((item) => (item.id === asset.id ? toSummary(asset) : item)),
    );
  }

  private schedulePoll(): void {
    if (this.pollTimer !== null) return;
    this.pollTimer = setTimeout(async () => {
      this.pollTimer = null;
      for (const id of this.pending()) {
        try {
          this.replace(await firstValueFrom(this.media.get(id)));
        } catch {
          // Asked again on the next tick.
        }
      }
      if (this.pending().length > 0) this.schedulePoll();
    }, POLL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
}

/** A full asset as a library tile, using the 400-wide WebP as the thumbnail. */
export function toSummary(asset: MediaAsset): MediaAssetSummary {
  return {
    id: asset.id,
    fileName: asset.fileName,
    mimeType: asset.mimeType,
    bytes: asset.bytes,
    uploadedBytes: asset.uploadedBytes,
    width: asset.width,
    height: asset.height,
    processingStatus: asset.processingStatus,
    visibility: asset.visibility,
    altText: asset.altText,
    credit: asset.credit,
    uploadedAt: asset.uploadedAt,
    thumbnailUrl: asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ?? null,
  };
}

function matches(filter: Filter, item: MediaAssetSummary): boolean {
  switch (filter) {
    case 'missing-alt':
      return !item.altText;
    case 'processing':
      return item.processingStatus === 'Pending';
    case 'failed':
      return item.processingStatus === 'Failed';
    default:
      return true;
  }
}
