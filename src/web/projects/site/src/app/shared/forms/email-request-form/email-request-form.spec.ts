import { TestBed } from '@angular/core/testing';
import {
  AccountEmailKind,
  AccountEmailResult,
  AccountService,
} from '../../../core/account.service';
import { EmailRequestForm } from './email-request-form';

class FakeAccount {
  requestEmail = vi.fn<(kind: AccountEmailKind, email: string) => Promise<AccountEmailResult>>(
    async () => ({ kind: 'sent' }),
  );
}

async function render(kind: AccountEmailKind) {
  const account = new FakeAccount();
  TestBed.configureTestingModule({ providers: [{ provide: AccountService, useValue: account }] });

  const fixture = TestBed.createComponent(EmailRequestForm);
  fixture.componentRef.setInput('kind', kind);
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;
  const input = () => element.querySelector<HTMLInputElement>('input[name="email"]')!;
  const ask = async (email: string) => {
    input().value = email;
    input().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };
  return { element, account, input, ask };
}

describe('EmailRequestForm', () => {
  it('gives the same answer whatever the address, because the gateway does', async () => {
    const { element, account, ask } = await render('password-reset');

    await ask(' ada@6311arts.test ');
    const first = element.querySelector('.account-panel__status')!.textContent;
    await ask('nobody@6311arts.test');

    expect(account.requestEmail).toHaveBeenNthCalledWith(1, 'password-reset', 'ada@6311arts.test');
    expect(account.requestEmail).toHaveBeenNthCalledWith(
      2,
      'password-reset',
      'nobody@6311arts.test',
    );
    expect(first).toContain('If an account uses that address');
    expect(element.querySelector('.account-panel__status')!.textContent).toBe(first);
  });

  it('asks for a confirmation link, promising nothing about the address', async () => {
    const { element, account, ask } = await render('email-confirmation');

    await ask('ada@6311arts.test');

    expect(account.requestEmail).toHaveBeenCalledWith('email-confirmation', 'ada@6311arts.test');
    expect(element.textContent).toContain('If that address has an account waiting to be confirmed');
    expect(element.textContent).toContain(
      'If you’ve already confirmed your address, you can sign in.',
    );
  });

  it('asks for an address before calling the gateway', async () => {
    const { element, account, ask, input } = await render('password-reset');

    await ask('not an address');

    expect(account.requestEmail).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Enter the email address you sign in with.');
    expect(input().getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(input());
  });

  it.each<[AccountEmailResult, string]>([
    [{ kind: 'rate-limited', retryAfterSeconds: 180 }, 'Wait 3 minutes before asking again.'],
    [{ kind: 'unavailable' }, 'Try again shortly.'],
  ])('explains %j', async (result, message) => {
    const { element, account, ask } = await render('password-reset');
    account.requestEmail.mockResolvedValue(result);

    await ask('ada@6311arts.test');

    expect(element.querySelector('ui-notice[role="alert"]')?.textContent).toContain(message);
  });

  it('posts, never gets, if the form is ever submitted natively', async () => {
    const { element } = await render('password-reset');

    expect(element.querySelector('form')?.getAttribute('method')).toBe('post');
  });
});
