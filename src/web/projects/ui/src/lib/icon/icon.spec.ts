import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ICON_NAMES, IconName, UiIcon } from './icon';

@Component({
  imports: [UiIcon],
  template: `<ui-icon [name]="name()" [size]="size()" [label]="label()" />`,
})
class Host {
  readonly name = signal<IconName>('arrow');
  readonly size = signal(24);
  readonly label = signal<string | undefined>(undefined);
}

function render() {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const icon = fixture.nativeElement.querySelector('ui-icon') as HTMLElement;
  return { fixture, icon };
}

describe('UiIcon', () => {
  it('is decorative by default and keeps the .icon host class the site styles target', () => {
    const { icon } = render();
    expect(icon.classList).toContain('icon');
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.hasAttribute('role')).toBe(false);
  });

  it('is announced as an image when labelled', () => {
    const { fixture, icon } = render();
    fixture.componentInstance.label.set('Cart');
    fixture.detectChanges();
    expect(icon.getAttribute('role')).toBe('img');
    expect(icon.getAttribute('aria-label')).toBe('Cart');
    expect(icon.hasAttribute('aria-hidden')).toBe(false);
  });

  it('sizes itself through --icon-size', () => {
    const { fixture, icon } = render();
    fixture.componentInstance.size.set(72);
    fixture.detectChanges();
    expect(icon.style.getPropertyValue('--icon-size')).toBe('72px');
  });

  it('draws a path for every glyph name', () => {
    const { fixture, icon } = render();
    for (const name of ICON_NAMES) {
      fixture.componentInstance.name.set(name);
      fixture.detectChanges();
      expect(icon.querySelector('path')?.getAttribute('d'), name).toMatch(/^M/);
    }
  });
});
