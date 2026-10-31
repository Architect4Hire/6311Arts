import { Injectable, computed, signal } from '@angular/core';
import { SitePreview } from '../models/site';

/**
 * Whether this render is a staff preview, and the draft it is showing (ADR 0036, 0063).
 *
 * The preview page puts the record here before it renders the real page component, and `PreviewSiteApi` hands it
 * back to whichever read that page makes — so the page itself knows nothing about previews. `Seo` reads the flag
 * too, because a draft is `noindex` whatever page is rendering it.
 *
 * It is per-render state, not a cache: the page clears it when it goes away, and nothing else ever writes it.
 */
@Injectable({ providedIn: 'root' })
export class PreviewContext {
  private readonly current = signal<SitePreview | null>(null);

  /** The draft being previewed, or `null` on every ordinary page. */
  readonly record = this.current.asReadonly();

  /** Whether a preview is on screen: what makes the head `noindex` and keeps the canonical off. */
  readonly active = computed(() => this.current() !== null);

  enter(preview: SitePreview): void {
    this.current.set(preview);
  }

  leave(): void {
    this.current.set(null);
  }
}
