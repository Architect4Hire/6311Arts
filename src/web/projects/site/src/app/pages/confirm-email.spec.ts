import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Meta } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { AccountService, LinkResult } from '../core/account.service';
import { LinkCredentials, LinkFragment } from '../core/link-fragment';
import { ConfirmEmail } from './confirm-email';

/** A made-up link, never a real credential. */
const LINK: LinkCredentials = { userId: 'user-1', token: 'CfDJ8-a-token-that-must-never-show' };

class FakeAccount {
  confirmEmail = vi.fn<(link: LinkCredentials) => Promise<LinkResult>>(async () => ({
    kind: 'done',
  }));
  requestEmail = vi.fn(async () => ({ kind: 'sent' as const }));
}

async function render(options: { link?: LinkCredentials | null; result?: LinkResult } = {}) {
  const account = new FakeAccount();
  if (options.result) account.confirmEmail.mockResolvedValueOnce(options.result);
  const responseInit: ResponseInit = {};
  const link = options.link === undefined ? LINK : options.link;

  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AccountService, useValue: account },
      { provide: LinkFragment, useValue: { take: vi.fn(() => link) } },
      { provide: RESPONSE_INIT, useValue: responseInit },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

  const fixture = TestBed.createComponent(ConfirmEmail);
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };
  await settle();

  const element = fixture.nativeElement as HTMLElement;
  return { element, account, navigate, responseInit, settle };
}

describe('ConfirmEmail', () => {
  it('is never indexed and never cached', async () => {
    const { responseInit } = await render();

    expect(TestBed.inject(Meta).getTag('name="robots"')?.content).toBe('noindex, follow');
    expect(new Headers(responseInit.headers).get('Cache-Control')).toBe('no-store');
  });

  it('confirms with the link as soon as it runs, and sends the person on to sign in', async () => {
    const { account, navigate, element } = await render();

    expect(account.confirmEmail).toHaveBeenCalledExactlyOnceWith(LINK);
    expect(navigate).toHaveBeenCalledWith(['/signin'], {
      state: { signinNotice: 'email-confirmed' },
    });
    expect(element.outerHTML).not.toContain(LINK.token);
  });

  it('offers a new confirmation link, and sign-in, when the link cannot be used', async () => {
    const { element, navigate } = await render({ result: { kind: 'link-invalid' } });

    expect(navigate).not.toHaveBeenCalled();
    expect(element.textContent).toContain('This link can’t be used');
    expect(element.querySelector('ui-notice a')?.getAttribute('href')).toBe('/signin');
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
  });

  it('offers a new link when the page has no link at all, without calling the gateway', async () => {
    const { element, account } = await render({ link: null });

    expect(account.confirmEmail).not.toHaveBeenCalled();
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
  });

  it('keeps the link through an outage and tries again on request', async () => {
    const { element, account, navigate, settle } = await render({
      result: { kind: 'unavailable' },
    });

    expect(element.textContent).toContain('Your link still works');
    element.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
    await settle();

    expect(account.confirmEmail).toHaveBeenCalledTimes(2);
    expect(account.confirmEmail).toHaveBeenLastCalledWith(LINK);
    expect(navigate).toHaveBeenCalled();
  });

  it('keeps the Try again button in place while the retry runs, so focus is not dropped', async () => {
    const { element, account, settle } = await render({ result: { kind: 'unavailable' } });
    let answer: (result: LinkResult) => void = () => undefined;
    account.confirmEmail.mockImplementationOnce(
      () => new Promise<LinkResult>((resolve) => (answer = resolve)),
    );

    const button = element.querySelector<HTMLButtonElement>('button[type="button"]')!;
    button.focus();
    button.click();
    await settle();

    expect(element.querySelector('button[type="button"]')).toBe(button);
    expect(button.textContent).toContain('Trying again…');
    expect(document.activeElement).toBe(button);

    answer({ kind: 'link-invalid' });
    await settle();

    // The button has gone now; focus goes to the heading rather than <body>.
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
    expect(document.activeElement).toBe(element.querySelector('h1'));
  });

  it('explains a rate limit in minutes', async () => {
    const { element } = await render({ result: { kind: 'rate-limited', retryAfterSeconds: 90 } });

    expect(element.querySelector('ui-notice[role="alert"]')?.textContent).toContain(
      'Wait 2 minutes',
    );
  });
});
