import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  ViewEncapsulation,
} from '@angular/core';
import { IconName, UiIcon } from '../icon/icon';

/** `neutral` informs, `positive` confirms, `pending` waits (dashed rule), `attention` needs a person. */
export type StatusTone = 'neutral' | 'positive' | 'pending' | 'attention';

/**
 * A small uppercase status label sized like the spike's `.tag`, but outlined on white rather than solid ink: a
 * tag labels content for visitors, a status chip reports system state to staff. Status is never colour alone,
 * so a glyph is required and the word is the projected content. It wraps rather than overflowing a narrow cell.
 *
 * ```html
 * <ui-status-chip tone="positive" icon="check">Published</ui-status-chip>
 * ```
 */
@Component({
  selector: 'ui-status-chip',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'ui-status', '[attr.data-tone]': 'tone()' },
  template: `<ui-icon [name]="icon()" [size]="12" [stroke]="3.2" /><span class="ui-status__word"
      ><ng-content
    /></span>`,
  styles: `
    .ui-status {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      // Optical padding for a 10px uppercase label: below the token scale on purpose, and no token
      // matches it. Tokenising to --space-1/--space-2 would change the chip's height (design-system.md
      // rule 6: match spike0 to the pixel).
      padding: 3px 7px;
      border: var(--border-thin) solid var(--ink);
      background: var(--white);
      color: var(--ink);
      font-size: var(--fs-micro);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      line-height: var(--lh-hint);
      text-transform: uppercase;
    }
    .ui-status[data-tone='neutral'] {
      border-color: var(--line);
      color: var(--muted);
    }
    .ui-status[data-tone='pending'] {
      border-style: dashed;
      border-color: var(--muted);
    }
    .ui-status[data-tone='attention'] {
      border-color: var(--hot);
      background: var(--accent);
    }
  `,
})
export class UiStatusChip {
  readonly tone = input<StatusTone>('neutral');
  readonly icon = input.required<IconName>();
}

/** Square sync state of a CMS record (design-system.md: Synced / Pending / Needs attention / Created in Square). */
export type SyncStatus = 'synced' | 'pending' | 'attention' | 'created-in-square';

export const SYNC_STATUS: Readonly<
  Record<SyncStatus, { label: string; tone: StatusTone; icon: IconName }>
> = {
  synced: { label: 'Synced', tone: 'positive', icon: 'check' },
  pending: { label: 'Pending', tone: 'pending', icon: 'clock' },
  attention: { label: 'Needs attention', tone: 'attention', icon: 'alert' },
  'created-in-square': { label: 'Created in Square', tone: 'neutral', icon: 'plus' },
};

/** The sync chip every Square-backed admin surface shows. */
@Component({
  selector: 'ui-sync-status',
  imports: [UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-chip [tone]="meta().tone" [icon]="meta().icon">{{
    meta().label
  }}</ui-status-chip>`,
})
export class UiSyncStatus {
  readonly status = input.required<SyncStatus>();
  protected readonly meta = computed(() => SYNC_STATUS[this.status()]);
}
