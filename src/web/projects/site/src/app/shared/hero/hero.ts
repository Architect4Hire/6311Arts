import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { VENUE_PHOTOS } from '../../data/venue-photos';

/**
 * The CSoon intro header: a full-bleed colour photo slideshow under a dark veil, the logo,
 * a big rotating headline and outlined buttons. `size="page"` gives the shorter banner used on inner pages.
 *
 * A page with no photos of its own falls back to `VENUE_PHOTOS`, so every hero carries a real photograph
 * of the building rather than a flat gradient.
 *
 * Rotation and slide changes start only in the browser, so the server HTML is stable and fully readable.
 */
@Component({
  selector: 'app-hero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'hero',
    '[class.hero--page]': 'size() === "page"',
    '[class.hero--full]': 'size() === "full"',
  },
  templateUrl: './hero.html',
})
export class Hero {
  title = input.required<string>();
  rotate = input<string[]>([]);
  eyebrow = input<string>();
  lead = input<string>();
  images = input<(string | undefined)[]>([]);
  size = input<'full' | 'page'>('page');
  logo = input(false);

  protected slides = computed(() => {
    const given = this.images().filter((s): s is string => !!s);
    return given.length ? given : VENUE_PHOTOS;
  });
  protected slide = signal(0);
  protected word = signal(0);

  constructor() {
    const destroy = inject(DestroyRef);
    afterNextRender(() => {
      if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
      const words = setInterval(
        () => this.word.update((i) => (i + 1) % Math.max(1, this.rotate().length)),
        2600,
      );
      const slides = setInterval(
        () => this.slide.update((i) => (i + 1) % Math.max(1, this.slides().length)),
        6500,
      );
      destroy.onDestroy(() => {
        clearInterval(words);
        clearInterval(slides);
      });
    });
  }
}
