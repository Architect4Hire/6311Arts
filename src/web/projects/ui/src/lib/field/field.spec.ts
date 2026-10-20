import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiControl, UiField } from './field';

@Component({
  imports: [UiField, UiControl],
  template: `
    <ui-field label="Email" [hint]="hint()" [error]="error()" [required]="required()">
      <input uiControl type="email" />
    </ui-field>
    <ui-field label="Notes" controlId="notes"><textarea uiControl></textarea></ui-field>
  `,
})
class Host {
  readonly hint = signal<string | undefined>(undefined);
  readonly error = signal<string | null>(null);
  readonly required = signal(false);
}

function render() {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const [field] = Array.from(root.querySelectorAll('ui-field'));
  const label = field.querySelector('label') as HTMLLabelElement;
  const input = field.querySelector('input') as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, root, field, label, input };
}

describe('UiField', () => {
  it('labels its control and gives every field its own id', () => {
    const { root, field, label, input } = render();
    expect(field.classList).toContain('field');
    expect(input.classList).toContain('field__control');
    expect(input.id).toMatch(/^ui-field-\d+$/);
    expect(label.htmlFor).toBe(input.id);
    expect(label.textContent?.trim()).toBe('Email');
    expect(root.querySelector('textarea')?.id).toBe('notes');
  });

  it('describes the control with its hint', () => {
    const { fixture, host, field, input } = render();
    expect(input.hasAttribute('aria-describedby')).toBe(false);
    host.hint.set('We reply within a day.');
    fixture.detectChanges();
    const hint = field.querySelector('.field__hint') as HTMLElement;
    expect(hint.textContent).toBe('We reply within a day.');
    expect(input.getAttribute('aria-describedby')).toBe(hint.id);
  });

  it('marks the control invalid and describes it with the error, next to the hint', () => {
    const { fixture, host, field, input } = render();
    host.hint.set('Hint');
    host.error.set('Enter an email address.');
    fixture.detectChanges();
    const error = field.querySelector('.field__error') as HTMLElement;
    expect(error.textContent?.trim()).toBe('Enter an email address.');
    expect(error.querySelector('ui-icon')).not.toBeNull();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(`${input.id}-hint ${input.id}-error`);

    host.error.set(null);
    fixture.detectChanges();
    expect(field.querySelector('.field__error')).toBeNull();
    expect(input.hasAttribute('aria-invalid')).toBe(false);
  });

  it('says "required" in words and sets aria-required', () => {
    const { fixture, host, label, input } = render();
    host.required.set(true);
    fixture.detectChanges();
    expect(label.querySelector('.field__req')?.textContent).toBe('required');
    expect(input.getAttribute('aria-required')).toBe('true');
  });
});
