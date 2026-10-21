import { HttpErrorResponse } from '@angular/common/http';
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
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { SessionService } from '../../../core/session/session.service';
import {
  COMPRESSION_PRESETS,
  CompressionPreset,
  MEDIA_CODES,
  MEDIA_LIMITS,
  MediaAsset,
  MediaAssetUpdate,
  PRESET_LABELS,
  decodeMediaProblem,
  formatBytes,
  savingPercent,
} from '../../../models/media';
import { ROLES } from '../../../models/session';
import { MediaService } from '../../../services/media.service';

/** How often an asset still being processed is asked about again. */
const POLL_MS = 3000;

interface Draft {
  readonly altText: string;
  readonly credit: string;
  readonly compressionPreset: CompressionPreset;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'conflict'; readonly theirs: MediaAsset }
  | { readonly kind: 'deleting' };

/**
 * One image: its alt text, credit and compression preset, what compression saved, its renditions, and (for Admins)
 * delete. Shown inside the library's dialog.
 *
 * Unsaved edits survive every recoverable failure (frontend.md): a refused save keeps the form as typed, and a 409
 * shows the saved version beside the editor's own so neither is lost.
 */
@Component({
  selector: 'cms-media-editor',
  imports: [FormsModule, UiButton, UiControl, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './media-editor.html',
  styleUrl: './media-editor.scss',
})
export class MediaEditor {
  readonly assetId = input.required<string>();
  /** The asset as saved, after a successful save. */
  readonly saved = output<MediaAsset>();
  readonly deleted = output<string>();
  readonly closed = output<void>();

  private readonly media = inject(MediaService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly altTextInput = viewChild<ElementRef<HTMLTextAreaElement>>('altTextInput');
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  /** What the persistent live region says: renditions finishing, a conflict, a saved edit. */
  protected readonly liveMessage = signal('');

  protected readonly status = signal<Status>({ kind: 'loading' });
  protected readonly asset = signal<MediaAsset | null>(null);
  protected readonly draft = signal<Draft>({
    altText: '',
    credit: '',
    compressionPreset: 'Balanced',
  });
  protected readonly altTextError = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly presets = COMPRESSION_PRESETS;
  protected readonly labels = PRESET_LABELS;
  protected readonly limits = MEDIA_LIMITS;
  protected readonly size = formatBytes;
  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));

  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });

  /** The 800-wide WebP, or whatever rendition exists, largest first. */
  protected readonly previewUrl = computed(() => {
    const renditions = this.asset()?.renditions ?? [];
    return (
      renditions.find((rendition) => rendition.name === '800.webp')?.url ??
      [...renditions].sort((a, b) => b.width - a.width)[0]?.url ??
      null
    );
  });

  protected readonly saving = computed(() => {
    const asset = this.asset();
    return asset ? savingPercent(asset.uploadedBytes, asset.bytes) : null;
  });

  /** Edits not yet saved: the library's unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const asset = this.asset();
    if (!asset) return false;
    const draft = this.draft();
    return (
      draft.altText.trim() !== (asset.altText ?? '') ||
      draft.credit.trim() !== (asset.credit ?? '') ||
      draft.compressionPreset !== asset.compressionPreset
    );
  });

  constructor() {
    effect(() => {
      this.assetId();
      untracked(() => void this.load());
    });
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  async load(): Promise<void> {
    this.status.set({ kind: 'loading' });
    try {
      const asset = await firstValueFrom(this.media.get(this.assetId()));
      this.asset.set(asset);
      this.draft.set(draftOf(asset));
      this.status.set({ kind: 'ready' });
      this.pollWhilePending();
    } catch {
      this.status.set({ kind: 'load-failed' });
    }
  }

  /**
   * While the Worker is still making the website sizes, asks again every few seconds, so the preview and status
   * appear without reopening the dialog. Only the asset is refreshed: the draft the person is typing is theirs.
   */
  private pollWhilePending(): void {
    this.stopPolling();
    if (this.asset()?.processingStatus !== 'Pending') return;

    this.pollTimer = setTimeout(async () => {
      this.pollTimer = null;
      try {
        const latest = await firstValueFrom(this.media.get(this.assetId()));
        const current = this.asset();
        if (current?.id !== latest.id) return;
        // Same row version: only the Worker's status and renditions moved, so the draft stays comparable.
        if (latest.etag === current.etag) this.asset.set(latest);
        if (latest.processingStatus === 'Ready')
          this.liveMessage.set('The website sizes are ready.');
        if (latest.processingStatus === 'Failed')
          this.liveMessage.set('Making the website sizes failed.');
      } catch {
        // Asked again on the next tick.
      }
      this.pollWhilePending();
    }, POLL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    if (change.altText !== undefined) this.altTextError.set(null);
  }

  protected presetLabel(preset: CompressionPreset): string {
    return PRESET_LABELS[preset].label;
  }

  protected async save(etag?: string): Promise<void> {
    const asset = this.asset();
    if (!asset || this.status().kind === 'saving' || !this.dirty()) return;

    this.formError.set(null);
    this.altTextError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      const saved = await firstValueFrom(
        this.media.update(asset.id, changesOf(asset, this.draft()), etag ?? asset.etag),
      );
      this.asset.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      this.liveMessage.set('Saved.');
      this.saved.emit(saved);
      this.pollWhilePending();
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  /** Saves the editor's own values over the version someone else saved. */
  protected async keepMine(theirs: MediaAsset): Promise<void> {
    this.asset.set(theirs);
    this.status.set({ kind: 'ready' });
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: MediaAsset): void {
    this.asset.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
  }

  protected async remove(): Promise<void> {
    const asset = this.asset();
    if (!asset) return;

    const confirmed = await this.confirm.confirm({
      heading: 'Delete this image?',
      message:
        'The image and all its website sizes are removed for good. Images used by content or the shop can’t be deleted.',
      confirmLabel: 'Delete image',
      cancelLabel: 'Keep it',
    });
    if (!confirmed) return;

    this.formError.set(null);
    this.status.set({ kind: 'deleting' });
    try {
      await firstValueFrom(this.media.delete(asset.id, asset.etag));
      this.deleted.emit(asset.id);
    } catch (error: unknown) {
      this.status.set({ kind: 'ready' });
      const problem =
        error instanceof HttpErrorResponse ? decodeMediaProblem(error.error, error.status) : null;
      this.formError.set(
        problem?.code === MEDIA_CODES.inUse
          ? 'This image is used by content or the shop, so it can’t be deleted. Remove it from there first.'
          : problem?.code === MEDIA_CODES.concurrencyConflict
            ? 'Someone changed this image a moment ago. Close it and open it again before deleting.'
            : 'The image couldn’t be deleted. Try again.',
      );
    }
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem =
      error instanceof HttpErrorResponse ? decodeMediaProblem(error.error, error.status) : null;

    if (problem?.code === MEDIA_CODES.concurrencyConflict) {
      try {
        const theirs = await firstValueFrom(this.media.get(this.assetId()));
        this.status.set({ kind: 'conflict', theirs });
        this.liveMessage.set(
          'Someone else saved this image while you were editing. Compare the two versions.',
        );
        // Focus follows the news, so a keyboard or screen-reader user lands on the comparison.
        afterNextRender(() => this.conflictBox()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return;
      } catch {
        // Fall through to the plain message; the draft is still in the form.
      }
    }

    this.status.set({ kind: 'ready' });
    const altError = problem?.fieldErrors['altText']?.[0];
    if (altError) {
      this.altTextError.set(altError);
      // The message is linked to the field and read when it has focus (UiField), so focus goes there.
      afterNextRender(() => this.altTextInput()?.nativeElement.focus(), {
        injector: this.injector,
      });
      return;
    }

    this.formError.set(
      problem?.status === 0 || problem === null
        ? 'Can’t reach 6311arts. Your changes are still here; try again.'
        : (problem.detail ?? 'The changes couldn’t be saved. Try again.'),
    );
  }
}

function draftOf(asset: MediaAsset): Draft {
  return {
    altText: asset.altText ?? '',
    credit: asset.credit ?? '',
    compressionPreset: asset.compressionPreset,
  };
}

/** Only what changed; an emptied field is sent as "" so the API clears it. */
function changesOf(asset: MediaAsset, draft: Draft): MediaAssetUpdate {
  const altText = draft.altText.trim();
  const credit = draft.credit.trim();
  return {
    ...(altText !== (asset.altText ?? '') ? { altText } : {}),
    ...(credit !== (asset.credit ?? '') ? { credit } : {}),
    ...(draft.compressionPreset !== asset.compressionPreset
      ? { compressionPreset: draft.compressionPreset }
      : {}),
  };
}
