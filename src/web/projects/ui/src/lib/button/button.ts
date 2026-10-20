import {
  afterNextRender,
  booleanAttribute,
  DestroyRef,
  Directive,
  ElementRef,
  inject,
  input,
} from '@angular/core';

export type ButtonVariant = 'border' | 'border-dark' | 'white' | 'dark';
export type ButtonSize = 'sm' | 'md' | 'lg';

/**
 * The template's square button on a native `<button>` or `<a>`, so `type`, form submission and `routerLink`
 * keep their native behaviour. Applies the `.btn` classes from `projects/ui/styles/global.scss`.
 *
 * `disabled` only takes effect on `<button>`; a link can't be disabled, so render a button instead.
 * `busy` keeps the button focusable (focus doesn't jump away mid-action) but swallows activation.
 */
@Directive({
  selector: 'button[uiButton], a[uiButton]',
  host: {
    class: 'btn',
    '[class.btn-border]': "variant() === 'border'",
    '[class.btn-border-dark]': "variant() === 'border-dark'",
    '[class.btn-white]': "variant() === 'white'",
    '[class.btn-dark]': "variant() === 'dark'",
    '[class.btn-sm]': "size() === 'sm'",
    '[class.btn-lg]': "size() === 'lg'",
    '[attr.disabled]': 'disabled() ? "" : null',
    '[attr.aria-busy]': 'busy() ? "true" : null',
    '[attr.aria-disabled]': 'busy() ? "true" : null',
  },
})
export class UiButton {
  /** `border` and `white` sit on dark or photo bands; `border-dark` and `dark` on light ones. */
  readonly variant = input<ButtonVariant>('border-dark');
  readonly size = input<ButtonSize>('md');
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Work in progress: sets aria-busy and ignores clicks and submits, keeping the label and focus in place. */
  readonly busy = input(false, { transform: booleanAttribute });

  constructor() {
    // A capture listener on the element runs before its bubble-phase (click) handlers, the consumer's included,
    // so stopping the event here swallows them, and preventDefault stops a submit. Browser-only, so SSR-safe.
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const guard = (event: Event) => {
        if (!this.busy()) return;
        event.preventDefault();
        event.stopImmediatePropagation();
      };
      element.addEventListener('click', guard, { capture: true });
      destroyRef.onDestroy(() => element.removeEventListener('click', guard, { capture: true }));
    });
  }
}
