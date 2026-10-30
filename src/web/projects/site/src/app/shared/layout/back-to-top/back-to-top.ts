import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';
import { UiIcon } from '@6311/ui';

/**
 * Fixed "Back to top" button on every page. It appears once the visitor is a screen's height down, and takes them
 * (and keyboard focus) back to the top of the page.
 *
 * The server renders it hidden — it never knows the scroll position — so hydration starts from the same state.
 * Focus lands on the header's skip link, the first stop on the page, so the next Tab carries on through the nav
 * instead of from a button that has just disappeared.
 */
@Component({
  selector: 'app-back-to-top',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiIcon],
  host: { class: 'to-top', '[class.to-top--shown]': 'shown()' },
  templateUrl: './back-to-top.html',
  styleUrl: './back-to-top.scss',
})
export class BackToTop {
  private readonly document = inject(DOCUMENT);
  protected readonly shown = signal(false);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const view = this.document.defaultView!;
      const onScroll = () => this.shown.set(view.scrollY > view.innerHeight);
      onScroll();
      view.addEventListener('scroll', onScroll, { passive: true });
      destroyRef.onDestroy(() => view.removeEventListener('scroll', onScroll));
    });
  }

  protected toTop(event: MouseEvent): void {
    const view = this.document.defaultView!;
    const still = view.matchMedia('(prefers-reduced-motion: reduce)').matches;
    view.scrollTo({ top: 0, behavior: still ? 'auto' : 'smooth' });
    // Keyboard activation only (a click count of 0): focusing the skip link reveals it, which a pointer user
    // never asked for.
    if (event.detail === 0) {
      this.document.querySelector<HTMLElement>('.skip')?.focus({ preventScroll: true });
    }
  }
}
