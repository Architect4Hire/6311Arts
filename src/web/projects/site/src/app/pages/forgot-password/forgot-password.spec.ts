import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Meta } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  async function render() {
    const responseInit: ResponseInit = {};
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AccountService, useValue: { requestEmail: vi.fn() } },
        { provide: RESPONSE_INIT, useValue: responseInit },
      ],
    });
    const fixture = TestBed.createComponent(ForgotPassword);
    await fixture.whenStable();
    return { element: fixture.nativeElement as HTMLElement, responseInit };
  }

  it('is never indexed and never cached', async () => {
    const { responseInit } = await render();

    expect(TestBed.inject(Meta).getTag('name="robots"')?.content).toBe('noindex, follow');
    expect(new Headers(responseInit.headers).get('Cache-Control')).toBe('no-store');
  });

  it('asks for a reset link, and leads back to sign-in', async () => {
    const { element } = await render();

    expect(element.querySelector('h1')?.textContent).toBe('Reset your password');
    expect(element.querySelector('app-email-request-form')).not.toBeNull();
    expect(element.querySelector('a[href="/signin"]')?.textContent).toBe('Back to sign-in');
  });
});
