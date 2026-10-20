import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { UiButton, UiIcon } from '@6311/ui';
import {
  MEDIA_CODES,
  MEDIA_LIMITS,
  MediaAsset,
  decodeMediaProblem,
  formatBytes,
  savingPercent,
} from '../../models/media';
import { MediaService } from '../../services/media.service';

type UploadState =
  | { readonly kind: 'queued' }
  | { readonly kind: 'uploading'; readonly percent: number | null }
  | { readonly kind: 'done'; readonly asset: MediaAsset }
  | { readonly kind: 'cancelled' }
  | { readonly kind: 'failed'; readonly message: string; readonly existingId: string | null };

interface Upload {
  readonly key: number;
  readonly file: File;
  readonly state: UploadState;
}

/**
 * Drag-and-drop or choose files; each one uploads in turn with its own progress bar, and each failure says what to
 * do about it. One at a time on purpose: the API compresses every upload as it arrives, and a queue of twenty
 * parallel 20 MB files would only make all of them slow.
 *
 * The drop zone is a label for a native file input, so it works from the keyboard and with a screen reader
 * without any custom key handling. Outcomes are announced through one live region that exists from the start,
 * because a region created together with its message is often not read out.
 */
@Component({
  selector: 'cms-media-uploader',
  imports: [UiButton, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="drop"
      [class.drop--over]="over()"
      (dragover)="onDragOver($event)"
      (dragleave)="over.set(false)"
      (drop)="onDrop($event)"
    >
      <ui-icon name="plus" [size]="28" [stroke]="1.6" />
      <label class="drop__label" for="media-upload-input">
        Drop images here, or <span class="drop__choose">choose files</span>
      </label>
      <span class="drop__hint" id="media-upload-hint"
        >JPEG, PNG or WebP, up to 25 MB each. Large photos are resized to 2560 px and compressed
        without visible loss; GPS location is removed.</span
      >
      <input
        #input
        id="media-upload-input"
        class="drop__input"
        type="file"
        multiple
        [accept]="accept"
        aria-describedby="media-upload-hint"
        (change)="onChoose(input)"
      />
    </div>

    <p class="sr-only" aria-live="polite">{{ announcement() }}</p>

    @if (uploads().length > 0) {
      <ul class="queue" role="list" aria-label="Uploads">
        @for (upload of uploads(); track upload.key) {
          <li class="item" [attr.data-state]="upload.state.kind">
            <span class="item__name">{{ upload.file.name }}</span>
            <span class="item__size">{{ size(upload.file.size) }}</span>
            @switch (upload.state.kind) {
              @case ('queued') {
                <span class="item__status">Waiting</span>
              }
              @case ('uploading') {
                <progress
                  class="item__bar"
                  max="100"
                  [attr.value]="percentOf(upload)"
                  [attr.aria-label]="'Uploading ' + upload.file.name"
                ></progress>
                <span class="item__status">{{ percentText(upload) }}</span>
                <button
                  uiButton
                  type="button"
                  size="sm"
                  [attr.aria-label]="'Cancel uploading ' + upload.file.name"
                  (click)="cancel(upload.key)"
                >
                  Cancel
                </button>
              }
              @case ('done') {
                <span class="item__status">
                  <ui-icon name="check" [size]="16" [stroke]="2.4" /> Uploaded{{
                    savedText(upload)
                  }}
                </span>
              }
              @case ('cancelled') {
                <span class="item__status">Cancelled</span>
              }
              @case ('failed') {
                <span class="item__status item__status--error">
                  <ui-icon name="stop" [size]="16" [stroke]="2.4" /><span class="sr-only"
                    >Failed:
                  </span>
                  {{ messageOf(upload) }}
                </span>
                @if (existingOf(upload); as existingId) {
                  <button
                    uiButton
                    type="button"
                    size="sm"
                    [attr.aria-label]="
                      'Show the image already in the library for ' + upload.file.name
                    "
                    (click)="showExisting.emit(existingId)"
                  >
                    Show it
                  </button>
                }
              }
            }
          </li>
        }
      </ul>
      @if (finished()) {
        <button uiButton type="button" size="sm" (click)="clearFinished()">Clear finished</button>
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }
    .drop {
      position: relative;
      display: grid;
      justify-items: center;
      gap: var(--space-2);
      padding: var(--space-6) var(--space-5);
      border: var(--border) dashed var(--line);
      background: var(--white);
      color: var(--text-soft);
      text-align: center;
    }
    .drop--over {
      border-color: var(--ink);
      background: var(--light);
    }
    .drop:focus-within {
      outline: var(--border) solid var(--focus);
      outline-offset: var(--focus-offset);
    }
    .drop__label {
      font-weight: var(--fw-bold);
      color: var(--ink);
      cursor: pointer;
    }
    .drop__choose {
      text-decoration: underline;
    }
    .drop__hint {
      max-width: 60ch;
      font-size: var(--fs-small);
    }
    /* Visually hidden but focusable: the label is the visible control. */
    .drop__input {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
    }
    .queue {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .item {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-2) var(--space-3);
      border: var(--border-thin) solid var(--line);
      background: var(--white);
      font-size: var(--fs-meta);
    }
    .item__name {
      flex: 1 1 12rem;
      min-width: 0;
      overflow-wrap: anywhere;
      font-weight: var(--fw-bold);
    }
    .item__size {
      color: var(--text-soft);
    }
    .item__bar {
      flex: 1 1 8rem;
      accent-color: var(--ink);
    }
    .item__status {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
    }
    .item__status--error {
      color: var(--danger);
    }
  `,
})
export class MediaUploader {
  private readonly media = inject(MediaService);
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');
  private nextKey = 1;
  private active: { key: number; subscription: Subscription } | null = null;

  /** Each stored asset, as soon as it is stored. */
  readonly uploaded = output<MediaAsset>();
  /** "Show it" on a duplicate: the asset already in the library. */
  readonly showExisting = output<string>();

  protected readonly uploads = signal<readonly Upload[]>([]);
  protected readonly over = signal(false);
  protected readonly announcement = signal('');
  protected readonly finished = computed(() =>
    this.uploads().some((upload) => !isLive(upload.state)),
  );
  protected readonly accept = MEDIA_LIMITS.accept;
  protected readonly size = formatBytes;

  /** Whether an upload is still waiting or in flight: leaving now would abandon it. */
  readonly busy = computed(() => this.uploads().some((upload) => isLive(upload.state)));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.active?.subscription.unsubscribe());
  }

  add(files: readonly File[]): void {
    const added = files.map((file): Upload => {
      const refusal = refuse(file);
      return {
        key: this.nextKey++,
        file,
        state: refusal
          ? { kind: 'failed', message: refusal, existingId: null }
          : { kind: 'queued' },
      };
    });
    this.uploads.update((uploads) => [...uploads, ...added]);
    const refused = added.filter((upload) => upload.state.kind === 'failed').length;
    if (refused > 0)
      this.announcement.set(`${refused} file(s) can’t be uploaded. See the list for why.`);
    this.pump();
  }

  protected onChoose(input: HTMLInputElement): void {
    this.add([...(input.files ?? [])]);
    // Cleared so choosing the same file again (after a failure) still fires change.
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.over.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    this.add([...(event.dataTransfer?.files ?? [])]);
  }

  protected cancel(key: number): void {
    if (this.active?.key === key) {
      this.active.subscription.unsubscribe();
      this.active = null;
    }
    this.setState(key, { kind: 'cancelled' });
    this.announcement.set('Upload cancelled.');
    // The Cancel button has gone; focus goes back to where uploads start.
    this.input().nativeElement.focus();
    this.pump();
  }

  protected clearFinished(): void {
    this.uploads.update((uploads) => uploads.filter((upload) => isLive(upload.state)));
    this.input().nativeElement.focus();
  }

  protected percentOf(upload: Upload): number | null {
    return upload.state.kind === 'uploading' ? upload.state.percent : null;
  }

  protected percentText(upload: Upload): string {
    const percent = this.percentOf(upload);
    return percent === null ? 'Uploading…' : `${percent}%`;
  }

  protected messageOf(upload: Upload): string {
    return upload.state.kind === 'failed' ? upload.state.message : '';
  }

  protected existingOf(upload: Upload): string | null {
    return upload.state.kind === 'failed' ? upload.state.existingId : null;
  }

  protected savedText(upload: Upload): string {
    return upload.state.kind === 'done' ? savedOf(upload.state.asset) : '';
  }

  /** Starts the next queued upload if none is running. */
  private pump(): void {
    if (this.active) return;
    const next = this.uploads().find((upload) => upload.state.kind === 'queued');
    if (!next) return;

    this.setState(next.key, { kind: 'uploading', percent: 0 });
    const subscription = this.media.upload(next.file).subscribe({
      next: (progress) => {
        if (progress.kind === 'progress') {
          const percent = progress.total
            ? Math.round((progress.loaded / progress.total) * 100)
            : null;
          this.setState(next.key, { kind: 'uploading', percent });
        } else {
          this.setState(next.key, { kind: 'done', asset: progress.asset });
          this.announcement.set(`${next.file.name} uploaded${savedOf(progress.asset)}.`);
          this.uploaded.emit(progress.asset);
        }
      },
      error: (error: unknown) => {
        const state = failure(error);
        this.setState(next.key, state);
        if (state.kind === 'failed') this.announcement.set(`${next.file.name}: ${state.message}`);
        this.active = null;
        this.pump();
      },
      complete: () => {
        this.active = null;
        this.pump();
      },
    });
    // A synchronous completion (a test's flush) has already cleared and moved on.
    if (!subscription.closed) this.active = { key: next.key, subscription };
  }

  private setState(key: number, state: UploadState): void {
    this.uploads.update((uploads) =>
      uploads.map((upload) => (upload.key === key ? { ...upload, state } : upload)),
    );
  }
}

function isLive(state: UploadState): boolean {
  return state.kind === 'queued' || state.kind === 'uploading';
}

function savedOf(asset: MediaAsset): string {
  const percent = savingPercent(asset.uploadedBytes, asset.bytes);
  return percent === null ? '' : ` — ${percent}% smaller (${formatBytes(asset.bytes)})`;
}

/** What the browser can tell before sending anything. The API checks again from the bytes. */
function refuse(file: File): string | null {
  if (file.size > MEDIA_LIMITS.maxUploadBytes) return 'Too large: images can be at most 25 MB.';
  if (file.type && !(MEDIA_LIMITS.types as readonly string[]).includes(file.type)) {
    return 'Not a JPEG, PNG or WebP image.';
  }
  return null;
}

function failure(error: unknown): UploadState {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'failed', message: 'The upload failed. Try again.', existingId: null };
  }
  if (error.status === 0) {
    return {
      kind: 'failed',
      message: 'Can’t reach 6311arts. Check your connection and try again.',
      existingId: null,
    };
  }

  const problem = decodeMediaProblem(error.error, error.status);
  const message = (() => {
    switch (problem.code) {
      case MEDIA_CODES.duplicate:
        return 'Already in the library.';
      case MEDIA_CODES.tooLarge:
      case MEDIA_CODES.payloadTooLarge:
        return 'Too large: images can be at most 25 MB.';
      case MEDIA_CODES.typeUnsupported:
        return problem.detail ?? 'Not a JPEG, PNG or WebP image.';
      case MEDIA_CODES.extensionMismatch:
        return 'The file’s name doesn’t match what it is. Rename it and try again.';
      case MEDIA_CODES.dimensionsOutOfRange:
      case MEDIA_CODES.undecodable:
        return problem.detail ?? 'This image can’t be used.';
      default:
        return problem.detail ?? `The upload failed (${error.status}). Try again.`;
    }
  })();

  return { kind: 'failed', message, existingId: problem.existingAssetId };
}
