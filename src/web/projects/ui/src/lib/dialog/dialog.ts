import {
  afterRenderEffect,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  inject,
  input,
  model,
  output,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { UiIcon } from '../icon/icon';

let nextId = 0;

/**
 * A modal dialog on the native `<dialog>` element: top layer, inert page, Escape to close.
 * `showModal()` runs only after render in the browser, so it is SSR-safe; the server renders it closed.
 * Focus returns to whatever had it when the dialog opened. Backdrop clicks are ignored unless
 * `closeOnBackdrop` is set, so a stray click can't discard work.
 * Its body is hidden until opened, so never put public site content in it: crawlers must see that content.
 *
 * ```html
 * <ui-dialog [(open)]="confirming" heading="Discard changes?">
 *   <p>Your edits to this artist haven't been saved.</p>
 *   <div uiDialogActions>
 *     <button uiButton (click)="confirming.set(false)">Keep editing</button>
 *     <button uiButton variant="dark" (click)="discard()">Discard</button>
 *   </div>
 * </ui-dialog>
 * ```
 */
@Component({
  selector: 'ui-dialog',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './dialog.html',
  styleUrl: './dialog.scss',
})
export class UiDialog {
  readonly open = model(false);
  readonly heading = input.required<string>();
  readonly closeOnBackdrop = input(false, { transform: booleanAttribute });
  /** Emits after the dialog closes for any reason (close button, Escape, backdrop or `open` set to false). */
  readonly closed = output<void>();

  protected readonly headingId = `ui-dialog-${nextId++}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly document = inject(DOCUMENT);
  private opener: HTMLElement | null = null;

  constructor() {
    afterRenderEffect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        const active = this.document.activeElement;
        this.opener = active instanceof HTMLElement ? active : null;
        dialog.showModal();
      } else if (!this.open() && dialog.open) {
        dialog.close();
      }
    });
  }

  protected onClose(): void {
    this.open.set(false);
    this.opener?.focus();
    this.opener = null;
    this.closed.emit();
  }

  /** A click whose target is the `<dialog>` itself landed on the backdrop: the panel's children cover the rest. */
  protected onClick(event: MouseEvent): void {
    if (this.closeOnBackdrop() && event.target === this.dialog().nativeElement)
      this.dialog().nativeElement.close();
  }
}
