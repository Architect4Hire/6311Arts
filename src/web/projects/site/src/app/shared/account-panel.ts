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
  template: `
    <section class="band band--light band--after-nav">
      <div class="container container--narrow account-panel">
        <h1 #headingRef class="band__title band__title--left" tabindex="-1">{{ heading() }}</h1>
        @if (lead()) {
          <p class="band__lead band__lead--left">{{ lead() }}</p>
        }
        <ng-content />
      </div>
    </section>
  `,
  styles: `
    app-account-panel {
      display: block;
    }
    .account-panel {
      display: grid;
      gap: var(--space-5);
      max-width: 520px;
    }
    .account-panel .band__title,
    .account-panel .band__lead {
      margin-bottom: 0;
    }
    .account-panel__status {
      display: grid;
      gap: var(--space-5);
    }
    /* Kept in the page while empty (it is the live region), without adding a gap to the layout. */
    .account-panel__status:empty {
      margin-bottom: calc(-1 * var(--space-5));
    }
    .account-panel__fields {
      display: grid;
      gap: var(--space-5);
      margin: 0;
      padding: 0;
      border: 0;
      min-width: 0;
    }
    .account-panel__fields p,
    .account-panel__aside {
      margin: 0;
    }
    .account-panel__aside {
      font-size: var(--fs-meta);
    }
    /* Links inside running text are underlined: the site's links carry no underline, and colour alone would not
       mark them (WCAG 1.4.1). */
    .account-panel__aside a,
    .account-panel .ui-notice a {
      text-decoration: underline;
      text-underline-offset: 0.2em;
    }
    .account-panel__check {
      display: flex;
      min-block-size: 40px;
      align-items: center;
      gap: var(--space-2);
      font-size: var(--fs-meta);
      cursor: pointer;
    }
    .account-panel__check input {
      width: 18px;
      height: 18px;
      margin: 0;
      accent-color: var(--ink);
    }
  `,
})
export class AccountPanel {
  readonly heading = input.required<string>();
  readonly lead = input<string>();

  private readonly headingElement = viewChild.required<ElementRef<HTMLHeadingElement>>('headingRef');

  focusHeading(): void {
    this.headingElement().nativeElement.focus();
  }
}
