import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NoticeTone, UiNotice } from './notice';

@Component({
  imports: [UiNotice],
  template: `
    <ui-notice
      [tone]="tone()"
      [heading]="heading()"
      [dismissible]="dismissible()"
      (dismissed)="dismissals = dismissals + 1"
    >
      <p>Saved a moment ago.</p>
    </ui-notice>
  `,
})
class Host {
  readonly tone = signal<NoticeTone>('info');
  readonly heading = signal<string | undefined>('Saved');
  readonly dismissible = signal(false);
  dismissals = 0;
}

function render() {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const notice = fixture.nativeElement.querySelector('ui-notice') as HTMLElement;
  return { fixture, host: fixture.componentInstance, notice };
}

describe('UiNotice', () => {
  it.each<[NoticeTone, string, string]>([
    ['info', 'status', 'Note'],
    ['success', 'status', 'Done'],
    ['warning', 'status', 'Warning'],
    ['error', 'alert', 'Error'],
  ])('%s notices use role %s and say "%s" to screen readers', (tone, role, word) => {
    const { fixture, host, notice } = render();
    host.tone.set(tone);
    fixture.detectChanges();
    expect(notice.getAttribute('role')).toBe(role);
    expect(notice.getAttribute('data-tone')).toBe(tone);
    expect(notice.querySelector('.sr-only')?.textContent).toBe(`${word}: `);
    expect(notice.querySelector('.ui-notice__glyph svg')).not.toBeNull();
  });

  it('draws warning and error with different glyphs, so they differ by more than colour', () => {
    const { fixture, host, notice } = render();
    const glyph = (tone: NoticeTone) => {
      host.tone.set(tone);
      fixture.detectChanges();
      return notice.querySelector('.ui-notice__glyph path')?.getAttribute('d');
    };
    expect(glyph('warning')).not.toBe(glyph('error'));
  });

  it('keeps the tone word when there is no heading', () => {
    const { fixture, host, notice } = render();
    host.heading.set(undefined);
    host.tone.set('error');
    fixture.detectChanges();
    expect(notice.querySelector('.ui-notice__heading')).toBeNull();
    expect(notice.querySelector('.sr-only')?.textContent).toBe('Error: ');
    expect(notice.textContent).toContain('Saved a moment ago.');
  });

  it('offers a labelled dismiss button only when dismissible', () => {
    const { fixture, host, notice } = render();
    expect(notice.querySelector('button')).toBeNull();
    host.dismissible.set(true);
    fixture.detectChanges();
    const close = notice.querySelector('button') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Dismiss');
    expect(close.type).toBe('button');
    close.click();
    expect(host.dismissals).toBe(1);
  });
});
