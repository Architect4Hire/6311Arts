import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Meta } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { AccountService, PasswordResetResult } from '../../core/account.service';
import { LinkCredentials, LinkFragment } from '../../core/link-fragment';
import { ResetPassword } from './reset-password';

/** Made-up test values, never real credentials. */
const LINK: LinkCredentials = { userId: 'user-1', token: 'CfDJ8-a-token-that-must-never-show' };
const NEW_PASSPHRASE = ['another', 'perfectly', 'fine', 'one'].join('-');

class FakeAccount {
  resetPassword = vi.fn<
    (link: LinkCredentials, newPassword: string) => Promise<PasswordResetResult>
  >(async () => ({ kind: 'done' }));
  requestEmail = vi.fn(async () => ({ kind: 'sent' as const }));
}

async function render(link: LinkCredentials | null = LINK) {
  const account = new FakeAccount();
  const responseInit: ResponseInit = {};
  const take = vi.fn(() => link);

  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AccountService, useValue: account },
      { provide: LinkFragment, useValue: { take } },
      { provide: RESPONSE_INIT, useValue: responseInit },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

  const fixture = TestBed.createComponent(ResetPassword);
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;
  const input = () => element.querySelector<HTMLInputElement>('input[name="newPassword"]')!;
  const choose = async (password: string) => {
    input().value = password;
    input().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };
  return { fixture, element, account, take, navigate, responseInit, input, choose };
}

describe('ResetPassword', () => {
  it('is never indexed and never cached', async () => {
    const { responseInit } = await render();

    expect(TestBed.inject(Meta).getTag('name="robots"')?.content).toBe('noindex, follow');
    expect(new Headers(responseInit.headers).get('Cache-Control')).toBe('no-store');
  });

  it('takes the link once, after rendering in the browser', async () => {
    const { take, input } = await render();

    expect(take).toHaveBeenCalledOnce();
    expect(input().disabled).toBe(false);
    expect(input().autocomplete).toBe('new-password');
    expect(input().type).toBe('password');
  });

  it('sets the new password with the link and sends the person on to sign in', async () => {
    const { account, navigate, choose, input } = await render();

    await choose(NEW_PASSPHRASE);

    expect(account.resetPassword).toHaveBeenCalledWith(LINK, NEW_PASSPHRASE);
    expect(navigate).toHaveBeenCalledWith(['/signin'], {
      state: { signinNotice: 'password-changed' },
    });
    expect(input().value).toBe('');
  });

  it('never puts the token in the page, the console or browser storage', async () => {
    const consoleSpies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation(() => undefined),
    );

    // auth.md: "tokens never appear in localStorage, sessionStorage, IndexedDB, URLs or logs." The page and the
    // console were asserted; storage was not, so a well-meant "remember the link across a reload" would have
    // passed (test-gap review). Spied rather than read back, to catch a write that is cleared again afterwards.
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => undefined);

    const { element, account, choose } = await render();
    account.resetPassword.mockResolvedValueOnce({ kind: 'unavailable' });

    await choose(NEW_PASSPHRASE);
    await choose(NEW_PASSPHRASE);

    expect(element.outerHTML).not.toContain(LINK.token);
    expect(JSON.stringify(setItem.mock.calls)).not.toContain(LINK.token);
    expect(JSON.stringify(setItem.mock.calls)).not.toContain(NEW_PASSPHRASE);
    setItem.mockRestore();

    for (const spy of consoleSpies) {
      expect(JSON.stringify(spy.mock.calls)).not.toContain(LINK.token);
      spy.mockRestore();
    }
  });

  it('shows the policy’s reasons and keeps the link for another try', async () => {
    const { element, account, choose, input } = await render();
    account.resetPassword.mockResolvedValueOnce({
      kind: 'password-rejected',
      reasons: ['A staff password must be at least 14 characters.'],
    });

    await choose('eight-chars');

    expect(element.textContent).toContain('A staff password must be at least 14 characters.');
    expect(input().getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(input());

    await choose(NEW_PASSPHRASE);
    expect(account.resetPassword).toHaveBeenLastCalledWith(LINK, NEW_PASSPHRASE);
  });

  it('checks the length it can before calling the gateway', async () => {
    const { element, account, choose } = await render();

    await choose('short');

    expect(account.resetPassword).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Use at least 8 characters.');
  });

  it('offers a fresh request when the link cannot be used, and forgets it', async () => {
    const { element, account, choose } = await render();
    account.resetPassword.mockResolvedValueOnce({ kind: 'link-invalid' });

    await choose(NEW_PASSPHRASE);

    expect(element.textContent).toContain('This link can’t be used');
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
    expect(element.querySelector('input[name="newPassword"]')).toBeNull();
    // The form that had focus is gone: focus goes to the heading, not <body> (WCAG 2.4.3).
    expect(document.activeElement).toBe(element.querySelector('h1'));
  });

  it('offers a fresh request when the page has no link at all', async () => {
    const { element, account } = await render(null);

    expect(account.resetPassword).not.toHaveBeenCalled();
    expect(element.textContent).toContain('This link can’t be used');
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
  });

  it('keeps the link through an outage', async () => {
    const { element, account, choose } = await render();
    account.resetPassword.mockResolvedValueOnce({ kind: 'unavailable' });

    await choose(NEW_PASSPHRASE);
    expect(element.textContent).toContain('Your link still works');

    await choose(NEW_PASSPHRASE);
    expect(account.resetPassword).toHaveBeenCalledTimes(2);
    expect(account.resetPassword).toHaveBeenLastCalledWith(LINK, NEW_PASSPHRASE);
  });
});
