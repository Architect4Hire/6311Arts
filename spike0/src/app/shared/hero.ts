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

/**
 * The CSoon intro header: a full-bleed black-and-white photo slideshow under a dark veil, the logo,
 * a big rotating headline and outlined buttons. `size="page"` gives the shorter banner used on inner pages.
 *
 * Rotation and slide changes start only in the browser, so the server HTML is stable and fully readable.
 */
@Component({
  selector: 'app-hero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'hero', '[class.hero--page]': 'size() === "page"', '[class.hero--full]': 'size() === "full"' },
  template: `
    <div class="hero__slides" aria-hidden="true">
      @for (s of slides(); track s; let i = $index) {
        <div class="hero__slide" [class.is-on]="i === slide()" [style.background-image]="'url(' + s + ')'"></div>
      }
    </div>
    <div class="hero__veil"></div>
    <div class="hero__body">
      @if (logo()) {
        <div class="mark hero__logo" aria-hidden="true"><span>6311</span><span>arts</span></div>
      }
      @if (eyebrow()) {
        <p class="hero__eyebrow">{{ eyebrow() }}</p>
      }
      <h1 class="hero__title">
        @if (rotate().length) {
          <span class="hero__static">{{ title() }}</span>
          <span class="hero__rotator" aria-live="off">
            @for (w of rotate(); track w; let i = $index) {
              <span class="hero__word" [class.is-on]="i === word()" [attr.aria-hidden]="i !== word()">{{ w }}</span>
            }
          </span>
          <span class="sr-only">{{ rotate().join(', ') }}</span>
        } @else {
          {{ title() }}
        }
      </h1>
      @if (lead()) {
        <p class="hero__lead">{{ lead() }}</p>
      }
      <div class="hero__ctas"><ng-content /></div>
    </div>
    @if (size() === 'full') {
      <a class="hero__scroll" href="#content" aria-label="Scroll to content"><span></span></a>
    }
  `,
})
export class Hero {
  title = input.required<string>();
  rotate = input<string[]>([]);
  eyebrow = input<string>();
  lead = input<string>();
  images = input<(string | undefined)[]>([]);
  size = input<'full' | 'page'>('page');
  logo = input(false);

  protected slides = computed(() => this.images().filter((s): s is string => !!s));
  protected slide = signal(0);
  protected word = signal(0);

  constructor() {
    const destroy = inject(DestroyRef);
    afterNextRender(() => {
      if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
      const words = setInterval(() => this.word.update((i) => (i + 1) % Math.max(1, this.rotate().length)), 2600);
      const slides = setInterval(() => this.slide.update((i) => (i + 1) % Math.max(1, this.slides().length)), 6500);
      destroy.onDestroy(() => {
        clearInterval(words);
        clearInterval(slides);
      });
    });
  }
}
