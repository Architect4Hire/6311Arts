import { Directive, ElementRef, afterNextRender, inject, input, OnDestroy } from '@angular/core';

/**
 * Fades an element in as it scrolls into view, like the template's WOW.js "fadeIn" but without jQuery.
 * Content is fully visible in the server-rendered HTML; the hidden state only applies once JavaScript runs
 * (see the `js` class set in index.html), so crawlers and no-JS visitors always see everything.
 */
@Directive({
  selector: '[reveal]',
  host: { class: 'reveal', '[style.--reveal-delay]': 'delay() + "ms"' },
})
export class Reveal implements OnDestroy {
  delay = input(0, { alias: 'reveal', transform: (v: unknown) => Number(v) || 0 });
  private el = inject(ElementRef<HTMLElement>);
  private io?: IntersectionObserver;

  constructor() {
    afterNextRender(() => {
      const node = this.el.nativeElement as HTMLElement;
      if (!('IntersectionObserver' in window)) {
        node.classList.add('is-in');
        return;
      }
      this.io = new IntersectionObserver(
        (entries) => {
          for (const e of entries) {
            if (e.isIntersecting) {
              node.classList.add('is-in');
              this.io?.disconnect();
            }
          }
        },
        { rootMargin: '0px 0px -8% 0px' },
      );
      this.io.observe(node);
    });
  }

  ngOnDestroy(): void {
    this.io?.disconnect();
  }
}
