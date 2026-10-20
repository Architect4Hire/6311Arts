import { TestBed } from '@angular/core/testing';
import { ToastHost } from './toast-host';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  it('folds a repeat of the same message into one', () => {
    const toasts = TestBed.inject(ToastService);
    toasts.show({ tone: 'error', heading: 'Can’t reach 6311arts' });
    toasts.show({ tone: 'error', heading: 'Can’t reach 6311arts' });

    expect(toasts.toasts()).toHaveLength(1);
  });

  it('keeps the newest four', () => {
    const toasts = TestBed.inject(ToastService);
    for (const n of [1, 2, 3, 4, 5]) toasts.show({ tone: 'error', heading: `Problem ${n}` });

    expect(toasts.toasts().map((toast) => toast.heading)).toEqual([
      'Problem 2',
      'Problem 3',
      'Problem 4',
      'Problem 5',
    ]);
  });
});

describe('ToastHost', () => {
  function render() {
    const fixture = TestBed.createComponent(ToastHost);
    fixture.detectChanges();
    return {
      fixture,
      host: fixture.nativeElement as HTMLElement,
      toasts: TestBed.inject(ToastService),
    };
  }

  it('is a live region before anything is shown', () => {
    const { host } = render();

    expect(host.getAttribute('aria-live')).toBe('polite');
    expect(host.querySelectorAll('ui-notice')).toHaveLength(0);
  });

  it('shows the heading, the message and the reference, and announces errors', async () => {
    const { fixture, host, toasts } = render();
    toasts.show({
      tone: 'error',
      heading: 'Not saved',
      message: 'Try again.',
      reference: 'abc123',
    });
    await fixture.whenStable();

    const notice = host.querySelector('ui-notice')!;
    expect(notice.getAttribute('role')).toBe('alert');
    expect(notice.textContent).toContain('Not saved');
    expect(notice.textContent).toContain('Try again.');
    expect(notice.textContent).toContain('Reference: abc123');
  });

  it('stays until dismissed', async () => {
    const { fixture, host, toasts } = render();
    toasts.show({ tone: 'error', heading: 'Not saved' });
    await fixture.whenStable();

    host.querySelector<HTMLButtonElement>('button[aria-label="Dismiss"]')!.click();
    await fixture.whenStable();

    expect(toasts.toasts()).toEqual([]);
    expect(host.querySelectorAll('ui-notice')).toHaveLength(0);
  });
});
