import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiStatusChip } from './status-chip';
import { SYNC_STATUS, SyncStatus, UiSyncStatus } from '../sync-status/sync-status';

@Component({
  imports: [UiSyncStatus, UiStatusChip],
  template: `
    <ui-sync-status [status]="status()" />
    <ui-status-chip tone="positive" icon="check">Published</ui-status-chip>
  `,
})
class Host {
  readonly status = signal<SyncStatus>('synced');
}

describe('UiStatusChip', () => {
  it('shows a glyph and a word, never colour alone', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const chip = fixture.nativeElement.querySelectorAll('ui-status-chip')[1] as HTMLElement;
    expect(chip.getAttribute('data-tone')).toBe('positive');
    expect(chip.querySelector('ui-icon path')?.getAttribute('d')).toMatch(/^M/);
    expect(chip.querySelector('.ui-status__word')?.textContent).toBe('Published');
  });
});

describe('UiSyncStatus', () => {
  it.each<[SyncStatus, string]>([
    ['synced', 'Synced'],
    ['pending', 'Pending'],
    ['attention', 'Needs attention'],
    ['created-in-square', 'Created in Square'],
  ])('%s reads "%s" with its own glyph and tone', (status, word) => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.status.set(status);
    fixture.detectChanges();
    const chip = fixture.nativeElement.querySelector(
      'ui-sync-status ui-status-chip',
    ) as HTMLElement;
    expect(chip.textContent?.trim()).toBe(word);
    expect(chip.getAttribute('data-tone')).toBe(SYNC_STATUS[status].tone);
    expect(chip.querySelector('ui-icon path')?.getAttribute('d')).toMatch(/^M/);
  });

  it('gives each sync status a distinct glyph', () => {
    const icons = Object.values(SYNC_STATUS).map((s) => s.icon);
    expect(new Set(icons).size).toBe(icons.length);
  });
});
