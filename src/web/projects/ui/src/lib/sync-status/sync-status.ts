import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IconName } from '../icon/icon';
import { StatusTone, UiStatusChip } from '../status-chip/status-chip';

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
  templateUrl: './sync-status.html',
})
export class UiSyncStatus {
  readonly status = input.required<SyncStatus>();
  protected readonly meta = computed(() => SYNC_STATUS[this.status()]);
}
