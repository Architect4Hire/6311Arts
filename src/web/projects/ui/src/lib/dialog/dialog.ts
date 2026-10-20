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
  template: `
    <dialog
      #dialog
      class="ui-dialog"
      [attr.aria-labelledby]="headingId"
      (close)="onClose()"
      (click)="onClick($event)"
    >
      <div class="ui-dialog__head">
        <h2 class="ui-dialog__title" [id]="headingId">{{ heading() }}</h2>
        <button type="button" class="ui-dialog__close" aria-label="Close" (click)="dialog.close()">
          <ui-icon name="close" [size]="18" [stroke]="2.4" />
        </button>
      </div>
      <div class="ui-dialog__body"><ng-content /></div>
      <div class="ui-dialog__actions"><ng-content select="[uiDialogActions]" /></div>
    </dialog>
  `,
  styles: `
    .ui-dialog {
      padding: 0;
      border: var(--border) solid var(--ink);
      border-radius: var(--radius);
      background: var(--white);
      color: var(--text);
      width: min(560px, calc(100vw - 2 * var(--pad)));
      max-height: calc(100dvh - 2 * var(--pad));
      overflow: auto;
      overscroll-behavior: contain;
    }
    .ui-dialog::backdrop {
      background: var(--veil);
    }
    .ui-dialog__head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-4);
      padding: var(--space-4) var(--space-5);
      border-bottom: var(--border-thin) solid var(--line);
    }
    .ui-dialog__title {
      margin: 0;
      font-size: var(--fs-small);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-wide);
      text-transform: uppercase;
      color: var(--ink);
    }
    /* a 40px target around an 18px glyph; the negative margin keeps the header's height and alignment */
    .ui-dialog__close {
      display: inline-grid;
      place-items: center;
      min-width: 40px;
      min-height: 40px;
      margin: -11px;
      padding: 0;
      border: 0;
      background: none;
      color: var(--ink);
      cursor: pointer;
    }
    .ui-dialog__body {
      padding: var(--space-5);
    }
    .ui-dialog__body > :last-child {
      margin-bottom: 0;
    }
    .ui-dialog__actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-3);
      padding: 0 var(--space-5) var(--space-5);
    }
    .ui-dialog__actions:empty {
      display: none;
    }
    .ui-dialog__actions > [uiDialogActions] {
      display: contents;
    }
  `,
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
