/*
 * @6311/ui public API: the tokens' companion primitives shared by the site and the CMS (design-system.md).
 * Import from '@6311/ui' only; never deep-import lib/ paths.
 * Styles: projects/ui/styles/tokens.scss and global.scss, loaded once per app through angular.json.
 */

export { type ButtonSize, type ButtonVariant, UiButton } from './lib/button/button';
export { UiDialog } from './lib/dialog/dialog';
export { UiEmptyState } from './lib/empty-state/empty-state';
export { UiControl, UiField } from './lib/field/field';
export { ICON_NAMES, type IconName, UiIcon } from './lib/icon/icon';
export { type NoticeTone, UiNotice } from './lib/notice/notice';
export {
  type StatusTone,
  SYNC_STATUS,
  type SyncStatus,
  UiStatusChip,
  UiSyncStatus,
} from './lib/status-chip/status-chip';
