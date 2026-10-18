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
  template: `
    @if (src() && !failed()) {
      <img #img [src]="src()" [alt]="alt()" [attr.loading]="priority() ? 'eager' : 'lazy'"
        [attr.fetchpriority]="priority() ? 'high' : null" decoding="async" (error)="failed.set(true)" />
    } @else {
      <div class="ph" [style.--a]="hue().a" [style.--b]="hue().b" [style.--h]="hue().h" [style.--h2]="hue().h2" role="img" [attr.aria-label]="alt()">
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
        radial-gradient(120% 90% at var(--a) 0%, hsl(var(--h) 85% 72% / .95), transparent 58%),
        radial-gradient(90% 80% at var(--b) 100%, hsl(var(--h2) 80% 42% / .9), transparent 62%),
        linear-gradient(160deg, hsl(var(--h) 70% 60%), hsl(var(--h2) 65% 38%));
    }
    .ph::after {
      content: ''; position: absolute; inset: 0; opacity: .16; mix-blend-mode: overlay;
      background-image: repeating-linear-gradient(115deg, rgba(255,255,255,.5) 0 1px, transparent 1px 7px);
    }
    .ph__mark { font-weight: 800; font-size: clamp(28px, 5vw, 56px); letter-spacing: -.02em; color: rgba(255,255,255,.55); text-transform: uppercase; }
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

  /** Stable per-title colour so a grid of placeholders doesn't look copy-pasted. */
  protected hue = computed(() => {
    let h = 0;
    for (const ch of this.alt()) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    const base = h % 360;
    return { a: `${h % 100}%`, b: `${(h >> 8) % 100}%`, h: `${base}`, h2: `${(base + 40 + ((h >> 4) % 90)) % 360}` };
  });

  constructor() {
    // A server-rendered <img> can fail before hydration attaches (error), so check once in the browser.
    afterNextRender(() => {
      const el = this.img()?.nativeElement;
      if (el && el.complete && el.naturalWidth === 0) this.failed.set(true);
    });
  }
}
