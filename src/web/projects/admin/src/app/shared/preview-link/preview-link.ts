import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiField, UiControl, UiNotice } from '@6311/ui';
import { GALLERY_TIME_ZONE } from '../../core/time/gallery-time';
import { PreviewService } from '../../services/preview.service';
import { PreviewLink, PreviewType } from '../../models/content';
import { decodeApiProblem } from '../../models/decode';

const EXPIRES = new Intl.DateTimeFormat('en-US', {
  timeZone: GALLERY_TIME_ZONE,
  timeStyle: 'short',
});

/**
 * "Preview": a signed, expiring link to the record as the site will show it (ADR 0036).
 *
 * ```html
 * <cms-preview-link entityType="artist" [entityId]="artist.id" [dirty]="dirty()" />
 * ```
 *
 * The link is shown, never stored: it is a credential, so it does not go into browser storage (auth.md "Token
 * storage"). It is read-only — it opens one draft and authorizes nothing — and it stops working after half an
 * hour, which the panel says.
 */
@Component({
  selector: 'cms-preview-link',
  imports: [UiButton, UiControl, UiField, UiNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './preview-link.html',
  styleUrl: './preview-link.scss',
})
export class PreviewLinkButton {
  readonly entityType = input.required<PreviewType>();
  readonly entityId = input.required<string>();
  /** Unsaved edits in the editor: a preview shows the saved version, and the panel says so. */
  readonly dirty = input(false);

  private readonly previews = inject(PreviewService);
  private readonly document = inject(DOCUMENT);

  protected readonly asking = signal(false);
  protected readonly link = signal<PreviewLink | null>(null);
  protected readonly failure = signal<string | null>(null);

  /** The whole address, so it can be pasted into a message; the anchor keeps the relative one. */
  protected readonly address = computed(() => {
    const made = this.link();
    if (!made) return '';
    const origin = this.document.location?.origin;
    return origin ? new URL(made.path, origin).href : made.path;
  });

  protected readonly announcement = computed(() => {
    if (this.asking()) return 'Making the preview link…';

    const made = this.link();
    if (!made) return '';

    const ms = Date.parse(made.expiresAt);
    return Number.isNaN(ms)
      ? `This link shows the ${this.entityType()} as the site will.`
      : `This link works until ${EXPIRES.format(ms)} gallery time, and shows the ${this.entityType()} as the site will.`;
  });

  protected async ask(): Promise<void> {
    if (this.asking()) return;

    this.asking.set(true);
    this.failure.set(null);
    try {
      this.link.set(await firstValueFrom(this.previews.link(this.entityType(), this.entityId())));
    } catch (error: unknown) {
      // Shown here rather than toasted, like every other refusal these screens handle themselves.
      this.link.set(null);
      const problem =
        error instanceof HttpErrorResponse ? decodeApiProblem(error.error, error.status) : null;
      this.failure.set(
        problem?.detail ?? 'The preview link could not be made. Try again in a moment.',
      );
    } finally {
      this.asking.set(false);
    }
  }

  /** Selects the whole address on focus, so copying it is one keystroke. */
  protected select(event: Event): void {
    (event.target as HTMLInputElement | null)?.select();
  }
}
