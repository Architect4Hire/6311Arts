import { TestBed } from '@angular/core/testing';
import { ConfirmHost } from './confirm-host';
import { ConfirmService } from './confirm.service';

const question = {
  heading: 'Discard unsaved changes?',
  message: 'Leaving now loses them.',
  confirmLabel: 'Discard changes',
  cancelLabel: 'Keep editing',
};

describe('ConfirmService', () => {
  it('answers a superseded question "no"', async () => {
    const confirm = TestBed.inject(ConfirmService);
    const first = confirm.confirm(question);
    const second = confirm.confirm({ ...question, heading: 'Second?' });

    expect(await first).toBe(false);
    confirm.answer(true);
    expect(await second).toBe(true);
  });
});

describe('ConfirmHost', () => {
  beforeAll(() => {
    // jsdom has no modal dialog; the primitive's own tests cover the real element.
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    };
  });

  async function ask() {
    const fixture = TestBed.createComponent(ConfirmHost);
    fixture.detectChanges();
    const answer = TestBed.inject(ConfirmService).confirm(question);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const button = (label: string) =>
      [...host.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    return { fixture, host, answer, button };
  }

  it('asks the question with both choices', async () => {
    const { host, button } = await ask();

    expect(host.querySelector('dialog')?.hasAttribute('open')).toBe(true);
    expect(host.querySelector('h2')?.textContent).toBe('Discard unsaved changes?');
    expect(button('Discard changes')).toBeTruthy();
    expect(button('Keep editing')).toBeTruthy();
  });

  it.each([
    ['Discard changes', true],
    ['Keep editing', false],
  ])('"%s" answers %s', async (label, expected) => {
    const { button, answer } = await ask();
    button(label).click();

    expect(await answer).toBe(expected);
  });

  it('treats closing the dialog as "no"', async () => {
    const { host, answer } = await ask();
    host.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!.click();

    expect(await answer).toBe(false);
  });
});
