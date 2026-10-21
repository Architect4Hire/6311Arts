import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewEncapsulation,
  input,
  viewChild,
} from '@angular/core';

/**
 * The frame the account pages share: `/signin` and the emailed-link pages (ADR 0027, 0029). A light band under the
 * nav, a narrow column, a left-aligned title and lead, then the page's own content.
 *
 * The page supplies its live region as `<div class="account-panel__status" aria-live="polite">`, present from the
 * first render so what appears in it later is announced (see `ui-notice`). The classes here are global (no view
 * encapsulation) so a page's own markup can use them.
 *
 * When a page swaps out the control that had focus (a form replaced by "ask for a new link"), it calls
 * {@link focusHeading} so keyboard focus lands at the top of the panel rather than on `<body>` (WCAG 2.4.3).
 */
@Component({
  selector: 'app-account-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './account-panel.html',
  styleUrl: './account-panel.scss',
})
export class AccountPanel {
  readonly heading = input.required<string>();
  readonly lead = input<string>();

  private readonly headingElement =
    viewChild.required<ElementRef<HTMLHeadingElement>>('headingRef');

  focusHeading(): void {
    this.headingElement().nativeElement.focus();
  }
}
