import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiDialog } from './dialog';

/*
 * jsdom has no <dialog> behaviour, so give it the two methods the component calls. The top layer, inert page and
 * Escape key are the browser's; what's under test is the component's open/close contract and focus return.
 */
function stubDialog(): void {
  const proto = HTMLDialogElement.prototype as Partial<HTMLDialogElement>;
  if (typeof proto.showModal === 'function') return;
  Object.assign(proto, {
    showModal(this: HTMLDialogElement) {
      this.setAttribute('open', '');
    },
    close(this: HTMLDialogElement) {
      if (!this.hasAttribute('open')) return;
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    },
  });
  if (!('open' in proto)) {
    Object.defineProperty(proto, 'open', {
      get(this: HTMLDialogElement) {
        return this.hasAttribute('open');
      },
    });
  }
}

@Component({
  imports: [UiDialog],
  template: `
    <button id="opener" (click)="open.set(true)">Delete</button>
    <ui-dialog
      [(open)]="open"
      heading="Delete this artist?"
      [closeOnBackdrop]="backdrop()"
      (closed)="closes = closes + 1"
    >
      <p>This can't be undone.</p>
      <div uiDialogActions><button id="confirm">Delete</button></div>
    </ui-dialog>
  `,
})
class Host {
  readonly open = signal(false);
  readonly backdrop = signal(false);
  closes = 0;
}

async function render() {
  stubDialog();
  const fixture = TestBed.createComponent(Host);
  document.body.appendChild(fixture.nativeElement);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const dialog = root.querySelector('dialog') as HTMLDialogElement;
  const opener = root.querySelector('#opener') as HTMLButtonElement;
  return { fixture, host: fixture.componentInstance, root, dialog, opener };
}

describe('UiDialog', () => {
  it('renders closed, labelled by its heading, with projected body and actions', async () => {
    const { dialog } = await render();
    expect(dialog.open).toBe(false);
    const heading = dialog.querySelector('h2') as HTMLElement;
    expect(heading.textContent).toBe('Delete this artist?');
    expect(dialog.getAttribute('aria-labelledby')).toBe(heading.id);
    expect(dialog.querySelector('.ui-dialog__body')?.textContent).toContain(
      "This can't be undone.",
    );
    expect(dialog.querySelector('.ui-dialog__actions #confirm')).not.toBeNull();
  });

  it('opens modally when open becomes true and closes when it becomes false', async () => {
    const { fixture, host, dialog } = await render();
    host.open.set(true);
    await fixture.whenStable();
    expect(dialog.open).toBe(true);
    host.open.set(false);
    await fixture.whenStable();
    expect(dialog.open).toBe(false);
    expect(host.closes).toBe(1);
  });

  it('closes from its labelled close button, updates the model and returns focus to the opener', async () => {
    const { fixture, host, dialog, opener } = await render();
    opener.focus();
    opener.click();
    await fixture.whenStable();
    expect(dialog.open).toBe(true);

    const close = dialog.querySelector('.ui-dialog__close') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Close');
    close.focus();
    close.click();
    await fixture.whenStable();
    expect(dialog.open).toBe(false);
    expect(host.open()).toBe(false);
    expect(host.closes).toBe(1);
    expect(document.activeElement).toBe(opener);
  });

  it('ignores backdrop clicks unless closeOnBackdrop is set', async () => {
    const { fixture, host, dialog } = await render();
    host.open.set(true);
    await fixture.whenStable();
    dialog.click();
    expect(dialog.open).toBe(true);

    host.backdrop.set(true);
    await fixture.whenStable();
    dialog.querySelector('p')?.click();
    expect(dialog.open).toBe(true);
    dialog.click();
    await fixture.whenStable();
    expect(dialog.open).toBe(false);
    expect(host.open()).toBe(false);
  });
});
