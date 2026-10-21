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
  templateUrl: './notice.html',
  styleUrl: './notice.scss',
})
export class UiNotice {
  readonly tone = input<NoticeTone>('info');
  readonly heading = input<string>();
  readonly dismissible = input(false, { transform: booleanAttribute });
  readonly dismissed = output<void>();
  protected readonly meta = computed(() => TONES[this.tone()]);
}
