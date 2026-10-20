import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { IconName } from '../icon/icon';
import { UiEmptyState } from './empty-state';

@Component({
  imports: [UiEmptyState],
  template: `
    <ui-empty-state heading="No artists yet" [icon]="icon()" [level]="level()">
      <p>Artists you add appear here.</p>
      <div uiEmptyActions><a href="new">Add an artist</a></div>
    </ui-empty-state>
  `,
})
class Host {
  readonly icon = signal<IconName | undefined>('frame');
  readonly level = signal<2 | 3>(2);
}

function render() {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const empty = fixture.nativeElement.querySelector('ui-empty-state') as HTMLElement;
  return { fixture, host: fixture.componentInstance, empty };
}

describe('UiEmptyState', () => {
  it('shows an icon, a heading, the explanation and the actions', () => {
    const { empty } = render();
    expect(empty.querySelector('ui-icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(empty.querySelector('h2')?.textContent).toBe('No artists yet');
    expect(empty.querySelector('.ui-empty__text')?.textContent).toContain(
      'Artists you add appear here.',
    );
    expect(empty.querySelector('.ui-empty__actions a')?.getAttribute('href')).toBe('new');
  });

  it('fits the page outline and works without an icon', () => {
    const { fixture, host, empty } = render();
    host.level.set(3);
    host.icon.set(undefined);
    fixture.detectChanges();
    expect(empty.querySelector('h2')).toBeNull();
    expect(empty.querySelector('h3')?.textContent).toBe('No artists yet');
    expect(empty.querySelector('ui-icon')).toBeNull();
  });
});
