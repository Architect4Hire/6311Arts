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
 * An artwork image that degrades to a monochrome placeholder tile when the photo is missing or fails to load,
 * so an unphotographed product never leaves a broken-image icon on the page.
 */
@Component({
  selector: 'app-art-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'art-image', '[class.is-failed]': 'failed()', '[style.aspect-ratio]': 'ratio()' },
  template: `
    @if (src() && !failed()) {
      <img #img [src]="src()" [alt]="alt()" [attr.loading]="priority() ? 'eager' : 'lazy'"
        [attr.fetchpriority]="priority() ? 'high' : null" decoding="async" (error)="failed.set(true)" />
    } @else {
      <div [class]="'ph ph--' + hue().tone" [style.--a]="hue().a" [style.--b]="hue().b" role="img" [attr.aria-label]="alt()">
        <span class="ph__mark">{{ initials() }}</span>
      </div>
    }
  `,
  styles: `
    :host { display: block; position: relative; overflow: hidden; background: #2a2f35; }
    img { width: 100%; height: 100%; object-fit: cover; display: block; transition: transform .8s cubic-bezier(.2,.7,.2,1), filter .6s; }
    .ph {
      position: absolute; inset: 0; display: grid; place-items: center;
      background:
        radial-gradient(120% 90% at var(--a) 0%, rgba(255,255,255,.16), transparent 55%),
        radial-gradient(90% 80% at var(--b) 100%, rgba(0,0,0,.55), transparent 60%),
        linear-gradient(160deg, #3a4048, #1b1f23);
    }
    .ph--1 { background-color: #d9d9d9; background-image:
        radial-gradient(120% 90% at var(--a) 0%, rgba(255,255,255,.7), transparent 55%),
        radial-gradient(90% 80% at var(--b) 100%, rgba(0,0,0,.18), transparent 60%),
        linear-gradient(160deg, #e6e6e6, #bdbdbd); }
    .ph--1 .ph__mark { color: rgba(0,0,0,.14); }
    .ph--2 { background-image:
        radial-gradient(120% 90% at var(--a) 0%, rgba(255,255,255,.28), transparent 55%),
        linear-gradient(160deg, #8c9096, #50555c); }
    .ph::after {
      content: ''; position: absolute; inset: 0; opacity: .18; mix-blend-mode: overlay;
      background-image: repeating-linear-gradient(115deg, rgba(255,255,255,.5) 0 1px, transparent 1px 7px);
    }
    .ph__mark { font-weight: 800; font-size: clamp(28px, 5vw, 56px); letter-spacing: -.02em; color: rgba(255,255,255,.22); text-transform: uppercase; }
  `,
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

  /** Stable per-title variation so a grid of placeholders doesn't look copy-pasted. */
  protected hue = computed(() => {
    let h = 0;
    for (const ch of this.alt()) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return { a: `${h % 100}%`, b: `${(h >> 8) % 100}%`, tone: (h >> 4) % 3 };
  });

  constructor() {
    // A server-rendered <img> can fail before hydration attaches (error), so check once in the browser.
    afterNextRender(() => {
      const el = this.img()?.nativeElement;
      if (el && el.complete && el.naturalWidth === 0) this.failed.set(true);
    });
  }
}
