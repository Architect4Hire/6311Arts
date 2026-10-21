import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  input,
  signal,
  viewChild,
} from '@angular/core';

/**
 * An artwork image that degrades to a coloured placeholder tile when the photo is missing or fails to load,
 * so an unphotographed product never leaves a broken-image icon — or a gray hole — on the page.
 *
 * The placeholder hue is derived from the title, so a grid of them reads as a set of paintings rather than
 * a set of errors, and the same piece gets the same colour on every render, server and browser alike.
 */
@Component({
  selector: 'app-art-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'art-image', '[class.is-failed]': 'failed()', '[style.aspect-ratio]': 'ratio()' },
  templateUrl: './art-image.html',
  styleUrl: './art-image.scss',
})
export class ArtImage {
  src = input<string | undefined>();
  alt = input('');
  ratio = input('4 / 5');
  priority = input(false);

  protected failed = signal(false);
  private img = viewChild<ElementRef<HTMLImageElement>>('img');

  protected initials = computed(() =>
    this.alt()
      .split(/\s+/)
      .filter((w) => /^[a-z0-9]/i.test(w))
      .slice(0, 2)
      .map((w) => w[0])
      .join(''),
  );

  /** Stable per-title colour so a grid of placeholders doesn't look copy-pasted. */
  protected hue = computed(() => {
    let h = 0;
    for (const ch of this.alt()) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    const base = h % 360;
    return {
      a: `${h % 100}%`,
      b: `${(h >> 8) % 100}%`,
      h: `${base}`,
      h2: `${(base + 40 + ((h >> 4) % 90)) % 360}`,
    };
  });

  constructor() {
    // A server-rendered <img> can fail before hydration attaches (error), so check once in the browser.
    afterNextRender(() => {
      const el = this.img()?.nativeElement;
      if (el && el.complete && el.naturalWidth === 0) this.failed.set(true);
    });
  }
}
