import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ButtonSize, ButtonVariant, UiButton } from './button';

@Component({
  imports: [UiButton],
  template: `
    <form (submit)="$event.preventDefault(); submits = submits + 1">
      <button
        uiButton
        (click)="clicks = clicks + 1"
        type="submit"
        [variant]="variant()"
        [size]="size()"
        [busy]="busy()"
        [disabled]="disabled()"
      >
        Save
      </button>
    </form>
    <a uiButton variant="border" href="/visit">Visit</a>
  `,
})
class Host {
  readonly variant = signal<ButtonVariant>('border-dark');
  readonly size = signal<ButtonSize>('md');
  readonly busy = signal(false);
  readonly disabled = signal(false);
  clicks = 0;
  submits = 0;
}

function render() {
  const fixture = TestBed.createComponent(Host);
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  const link = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;
  return { fixture, host: fixture.componentInstance, button, link };
}

describe('UiButton', () => {
  it('applies the spike classes for the default variant and size', () => {
    const { button } = render();
    expect([...button.classList].sort()).toEqual(['btn', 'btn-border-dark']);
    expect(button.type).toBe('submit');
  });

  it.each<[ButtonVariant, ButtonSize, string[]]>([
    ['border', 'sm', ['btn', 'btn-border', 'btn-sm']],
    ['white', 'lg', ['btn', 'btn-lg', 'btn-white']],
    ['dark', 'md', ['btn', 'btn-dark']],
  ])('maps variant %s and size %s onto classes', (variant, size, classes) => {
    const { fixture, host, button } = render();
    host.variant.set(variant);
    host.size.set(size);
    fixture.detectChanges();
    expect([...button.classList].sort()).toEqual(classes);
  });

  it('stays focusable while busy but swallows clicks and submits', () => {
    const { fixture, host, button } = render();
    host.busy.set(true);
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.getAttribute('aria-disabled')).toBe('true');
    button.focus();
    expect(document.activeElement).toBe(button);
    button.click();
    expect(host.clicks).toBe(0);
    expect(host.submits).toBe(0);

    host.busy.set(false);
    fixture.detectChanges();
    expect(button.hasAttribute('aria-busy')).toBe(false);
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    button.click();
    expect(host.clicks).toBe(1);
    expect(host.submits).toBe(1);
  });

  it('owns the disabled state', () => {
    const { fixture, host, button } = render();
    host.disabled.set(true);
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
  });

  it('styles links without changing their semantics', () => {
    const { link } = render();
    expect([...link.classList].sort()).toEqual(['btn', 'btn-border']);
    expect(link.getAttribute('href')).toBe('/visit');
    expect(link.hasAttribute('disabled')).toBe(false);
  });
});
