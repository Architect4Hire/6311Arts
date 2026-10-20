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
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
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
} from '../../models/media';
import { ROLES } from '../../models/session';
import { MediaService } from '../../services/media.service';

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
  template: `
    <!-- Exists from the start, so what is written into it later is read out. -->
    <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

    @switch (status().kind) {
      @case ('loading') {
        <p role="status">Loading image…</p>
      }
      @case ('load-failed') {
        <ui-notice tone="error" heading="This image couldn’t be loaded">
          <p>It may have been deleted, or 6311arts can’t be reached.</p>
          <button uiButton type="button" size="sm" (click)="load()">Try again</button>
        </ui-notice>
      }
      @default {
        @if (asset(); as asset) {
          <div class="layout">
            <figure class="preview">
              @if (previewUrl(); as url) {
                <img [src]="url" [alt]="draft().altText || ''" />
              } @else {
                <div class="preview__pending">
                  {{
                    asset.processingStatus === 'Failed'
                      ? 'Renditions failed'
                      : 'Renditions are being made…'
                  }}
                </div>
              }
              <figcaption class="facts">
                <span>{{ asset.width }}×{{ asset.height }} · {{ asset.mimeType }}</span>
                <span>
                  Stored {{ size(asset.bytes) }}
                  @if (saving(); as percent) {
                    — {{ percent }}% smaller than the {{ size(asset.uploadedBytes ?? 0) }} upload
                  }
                </span>
                <span class="facts__chips">
                  @switch (asset.processingStatus) {
                    @case ('Pending') {
                      <ui-status-chip tone="pending" icon="clock">Processing</ui-status-chip>
                    }
                    @case ('Failed') {
                      <ui-status-chip tone="attention" icon="alert">Failed</ui-status-chip>
                    }
                    @default {
                      <ui-status-chip tone="positive" icon="check">Ready</ui-status-chip>
                    }
                  }
                  <ui-status-chip
                    [tone]="asset.visibility === 'Published' ? 'positive' : 'neutral'"
                    icon="info"
                    >{{
                      asset.visibility === 'Published' ? 'Published' : 'Draft'
                    }}</ui-status-chip
                  >
                </span>
              </figcaption>
            </figure>

            <form class="form" novalidate (ngSubmit)="save()">
              @if (conflict(); as theirs) {
                <div #conflictBox class="conflict" tabindex="-1">
                <ui-notice
                  tone="warning"
                  heading="Someone else saved this image while you were editing"
                >
                  <table class="compare">
                    <caption class="sr-only">
                      Your changes and the saved version
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col">Field</th>
                        <th scope="col">Yours</th>
                        <th scope="col">Saved</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <th scope="row">Alt text</th>
                        <td>{{ draft().altText || '—' }}</td>
                        <td>{{ theirs.altText || '—' }}</td>
                      </tr>
                      <tr>
                        <th scope="row">Credit</th>
                        <td>{{ draft().credit || '—' }}</td>
                        <td>{{ theirs.credit || '—' }}</td>
                      </tr>
                      <tr>
                        <th scope="row">Compression</th>
                        <td>{{ presetLabel(draft().compressionPreset) }}</td>
                        <td>{{ presetLabel(theirs.compressionPreset) }}</td>
                      </tr>
                    </tbody>
                  </table>
                  <div class="actions">
                    <button
                      uiButton
                      type="button"
                      variant="dark"
                      size="sm"
                      (click)="keepMine(theirs)"
                    >
                      Keep my changes
                    </button>
                    <button uiButton type="button" size="sm" (click)="useTheirs(theirs)">
                      Use the saved version and discard mine
                    </button>
                  </div>
                </ui-notice>
                </div>
              }

              @if (formError(); as message) {
                <ui-notice tone="error">{{ message }}</ui-notice>
              }

              <ui-field
                label="Alt text"
                hint="Describe what’s in the picture for someone who can’t see it. Needed before the image can go on published content."
                [error]="altTextError()"
              >
                <textarea
                  #altTextInput
                  uiControl
                  name="altText"
                  rows="3"
                  [attr.maxlength]="limits.altTextMaxLength"
                  [ngModel]="draft().altText"
                  (ngModelChange)="patch({ altText: $event })"
                ></textarea>
              </ui-field>

              <ui-field
                label="Credit"
                hint="Who made or owns the image, as it should appear beside it."
              >
                <input
                  uiControl
                  name="credit"
                  [attr.maxlength]="limits.creditMaxLength"
                  [ngModel]="draft().credit"
                  (ngModelChange)="patch({ credit: $event })"
                />
              </ui-field>

              <fieldset class="presets">
                <legend>Compression for the website</legend>
                @for (preset of presets; track preset) {
                  <label class="preset">
                    <input
                      type="radio"
                      name="compressionPreset"
                      [value]="preset"
                      [checked]="draft().compressionPreset === preset"
                      (change)="patch({ compressionPreset: preset })"
                    />
                    <span>
                      <span class="preset__label">{{ labels[preset].label }}</span>
                      <span class="preset__hint">{{ labels[preset].hint }}</span>
                    </span>
                  </label>
                }
                <p class="presets__note">
                  Changing this re-makes the website sizes. The stored master stays as it is.
                </p>
              </fieldset>

              <div class="actions">
                <button
                  uiButton
                  type="submit"
                  variant="dark"
                  [busy]="status().kind === 'saving'"
                  [disabled]="!dirty()"
                >
                  {{ status().kind === 'saving' ? 'Saving…' : 'Save' }}
                </button>
                <button uiButton type="button" (click)="closed.emit()">Close</button>
                @if (isAdmin()) {
                  <button
                    uiButton
                    type="button"
                    class="actions__delete"
                    [busy]="status().kind === 'deleting'"
                    (click)="remove()"
                  >
                    Delete image
                  </button>
                }
              </div>
            </form>
          </div>

          @if (asset.renditions.length > 0) {
            <table class="renditions">
              <caption>
                Website sizes
              </caption>
              <thead>
                <tr>
                  <th scope="col">Size</th>
                  <th scope="col">Dimensions</th>
                  <th scope="col">File</th>
                </tr>
              </thead>
              <tbody>
                @for (rendition of asset.renditions; track rendition.name) {
                  <tr>
                    <th scope="row">
                      <a [href]="rendition.url" target="_blank" rel="noopener"
                        >{{ rendition.name }}<span class="sr-only"> (opens in a new tab)</span></a
                      >
                    </th>
                    <td>{{ rendition.width }}×{{ rendition.height }}</td>
                    <td>{{ size(rendition.bytes) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        }
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }
    .conflict:focus {
      outline: none;
    }
    .layout {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr));
      gap: var(--space-5);
      align-items: start;
    }
    .preview {
      display: grid;
      gap: var(--space-2);
      margin: 0;
    }
    .preview img {
      width: 100%;
      height: auto;
      max-height: 50vh;
      object-fit: contain;
      background: var(--light);
    }
    .preview__pending {
      display: grid;
      place-items: center;
      aspect-ratio: 4 / 3;
      background: var(--light);
      color: var(--text-soft);
    }
    .facts {
      display: grid;
      gap: var(--space-1);
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .facts__chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    .form {
      display: grid;
      gap: var(--space-4);
    }
    .presets {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      border: 0;
    }
    .presets legend {
      margin-bottom: var(--space-2);
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .preset {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
      cursor: pointer;
    }
    .preset input {
      margin-top: var(--space-1);
      accent-color: var(--ink);
    }
    .preset__label {
      display: block;
      font-weight: var(--fw-bold);
    }
    .preset__hint,
    .presets__note {
      display: block;
      margin: 0;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .actions__delete {
      margin-left: auto;
    }
    .compare,
    .renditions {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-small);
    }
    .compare th,
    .compare td,
    .renditions th,
    .renditions td {
      padding: var(--space-1) var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
    .renditions caption {
      margin-bottom: var(--space-2);
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-align: left;
      text-transform: uppercase;
    }
  `,
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
        if (latest.processingStatus === 'Ready') this.liveMessage.set('The website sizes are ready.');
        if (latest.processingStatus === 'Failed') this.liveMessage.set('Making the website sizes failed.');
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
        this.liveMessage.set('Someone else saved this image while you were editing. Compare the two versions.');
        // Focus follows the news, so a keyboard or screen-reader user lands on the comparison.
        afterNextRender(() => this.conflictBox()?.nativeElement.focus(), { injector: this.injector });
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
      afterNextRender(() => this.altTextInput()?.nativeElement.focus(), { injector: this.injector });
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
