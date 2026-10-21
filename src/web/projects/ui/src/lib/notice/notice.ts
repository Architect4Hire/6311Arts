import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
  ViewEncapsulation,
} from '@angular/core';
import { IconName, UiIcon } from '../icon/icon';

export type NoticeTone = 'info' | 'success' | 'warning' | 'error';

const TONES: Record<NoticeTone, { icon: IconName; word: string }> = {
  info: { icon: 'info', word: 'Note' },
  success: { icon: 'check', word: 'Done' },
  warning: { icon: 'alert', word: 'Warning' },
  error: { icon: 'stop', word: 'Error' },
};

/**
 * An inline message styled after the spike's form panel: a heavy left rule on pale gray.
 * Tone shows as a glyph, a rule colour and a screen-reader word, never colour alone.
 * Errors are announced assertively (role="alert"); everything else politely (role="status"). Screen readers
 * announce reliably only when content changes inside a region that already exists, so for a result that appears
 * after an action, render the ui-notice up front and swap its content, or place it in a persistent live container.
 * Dismissing emits `dismissed`; the parent removes the notice.
 */
@Component({
  selector: 'ui-notice',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: {
    class: 'ui-notice',
    '[attr.data-tone]': 'tone()',
    '[attr.role]': "tone() === 'error' ? 'alert' : 'status'",
  },
  template: `
    <ui-icon class="ui-notice__glyph" [name]="meta().icon" [size]="20" [stroke]="2" />
    <div class="ui-notice__body">
      @if (heading()) {
        <p class="ui-notice__heading">
          <span class="sr-only">{{ meta().word }}: </span>{{ heading() }}
        </p>
      } @else {
        <span class="sr-only">{{ meta().word }}: </span>
      }
      <ng-content />
    </div>
    @if (dismissible()) {
      <button
        type="button"
        class="ui-notice__close"
        aria-label="Dismiss"
        (click)="dismissed.emit()"
      >
        <ui-icon name="close" [size]="16" [stroke]="2.4" />
      </button>
    }
  `,
  styles: `
    .ui-notice {
      display: flex;
      align-items: flex-start;
      gap: var(--space-3);
      border-left: var(--border-accent) solid var(--ink);
      background: var(--light);
      color: var(--ink);
      padding: var(--space-4) var(--space-5);
      font-size: var(--fs-meta);
      line-height: var(--lh-hint);
    }
    .ui-notice[data-tone='warning'] {
      border-left-color: var(--hot);
    }
    .ui-notice[data-tone='error'] {
      border-left-color: var(--danger);
    }
    .ui-notice[data-tone='error'] .ui-notice__glyph {
      color: var(--danger);
    }
    .ui-notice__glyph {
      flex: none;
    }
    .ui-notice__body {
      flex: 1;
      min-width: 0;
    }
    .ui-notice__body > :last-child {
      margin-bottom: 0;
    }
    .ui-notice__heading {
      font-size: var(--fs-button);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-wide);
      text-transform: uppercase;
      margin: 2px 0 var(--space-1);
    }
    /* a 40px target around a 16px glyph; the negative margin keeps the notice's padding visually intact */
    .ui-notice__close {
      flex: none;
      display: inline-grid;
      place-items: center;
      min-width: 40px;
      min-height: 40px;
      padding: 0;
      // Pulls the 40px hit area back over the notice's own padding so the glyph stays optically aligned.
      // A literal, not calc(var(--space-3) * -1): this notice renders on the site *and* in the compact CMS,
      // where --space-3 is 8px, which would shift it (design-system.md rule 6).
      margin: -12px;
      border: 0;
      background: none;
      cursor: pointer;
    }
  `,
})
export class UiNotice {
  readonly tone = input<NoticeTone>('info');
  readonly heading = input<string>();
  readonly dismissible = input(false, { transform: booleanAttribute });
  readonly dismissed = output<void>();
  protected readonly meta = computed(() => TONES[this.tone()]);
}
