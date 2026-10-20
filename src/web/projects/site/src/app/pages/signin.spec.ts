import { provideHttpClient } from '@angular/common/http';
import { RESPONSE_INIT } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meta } from '@angular/platform-browser';
import { Navigation, Router, provideRouter } from '@angular/router';
import { BffSessionService, Session, SignInResult } from '../core/bff-session.service';
import { BrowserLocation } from '../core/browser-location';
import { Signin } from './signin';

class FakeSessions {
  current = vi.fn<() => Promise<Session | null>>(async () => null);
  signIn = vi.fn<(email: string, password: string) => Promise<SignInResult>>();
}

async function render(
  options: { returnUrl?: string; current?: Session | null; arrivedWith?: unknown } = {},
) {
  const sessions = new FakeSessions();
  sessions.current.mockResolvedValue(options.current ?? null);
  const responseInit: ResponseInit = {};

  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([]),
      { provide: BffSessionService, useValue: sessions },
      { provide: RESPONSE_INIT, useValue: responseInit },
    ],
  });
  const assign = vi
    .spyOn(TestBed.inject(BrowserLocation), 'assign')
    .mockImplementation(() => undefined);

  if (options.arrivedWith !== undefined) {
    vi.spyOn(TestBed.inject(Router), 'currentNavigation').mockReturnValue({
      extras: { state: { signinNotice: options.arrivedWith } },
    } as unknown as Navigation);
  }

  const fixture: ComponentFixture<Signin> = TestBed.createComponent(Signin);
  if (options.returnUrl !== undefined)
    fixture.componentRef.setInput('returnUrl', options.returnUrl);
  await fixture.whenStable();

  const element = fixture.nativeElement as HTMLElement;
  const input = (name: string) => element.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
  const type = async (name: string, value: string) => {
    input(name).value = value;
    input(name).dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const submit = async () => {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };
  return { fixture, element, sessions, assign, responseInit, input, type, submit };
}

const staff = { displayName: 'Ada', roles: ['Editor'] };

describe('Signin', () => {
  it('is never indexed and never cached', async () => {
    const { responseInit } = await render();

    expect(TestBed.inject(Meta).getTag('name="robots"')?.content).toBe('noindex, follow');
    expect(new Headers(responseInit.headers).get('Cache-Control')).toBe('no-store');
  });

  it('labels its fields and lets password managers fill them', async () => {
    const { element, input } = await render();

    expect(element.querySelector('h1')?.textContent).toBe('Staff sign-in');
    expect(element.querySelector(`label[for="${input('email').id}"]`)?.textContent).toContain(
      'Email',
    );
    expect(element.querySelector(`label[for="${input('password').id}"]`)?.textContent).toContain(
      'Password',
    );
    expect(input('email').autocomplete).toBe('username');
    expect(input('password').autocomplete).toBe('current-password');
    expect(input('password').type).toBe('password');
  });

  it('posts, never gets, if the form is ever submitted natively', async () => {
    const { element } = await render();

    expect(element.querySelector('form')?.getAttribute('method')).toBe('post');
  });

  it('asks for what is missing, and does not call the gateway', async () => {
    const { element, sessions, submit, input } = await render();
    await submit();

    expect(sessions.signIn).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Enter the email address you sign in with.');
    expect(input('email').getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(input('email'));
  });

  it('signs in and leaves for the CMS address the gateway asked for', async () => {
    const { sessions, assign, type, submit } = await render({ returnUrl: '/admin/classes' });
    sessions.signIn.mockResolvedValue({ kind: 'signed-in', session: staff });

    await type('email', ' ada@6311arts.test ');
    await type('password', 'correct horse battery');
    await submit();

    expect(sessions.signIn).toHaveBeenCalledWith('ada@6311arts.test', 'correct horse battery');
    expect(assign).toHaveBeenCalledWith('/admin/classes');
  });

  it('refuses to leave for another origin', async () => {
    const { sessions, assign, type, submit } = await render({ returnUrl: '//evil.example/admin' });
    sessions.signIn.mockResolvedValue({ kind: 'signed-in', session: staff });

    await type('email', 'ada@6311arts.test');
    await type('password', 'pw');
    await submit();

    expect(assign).toHaveBeenCalledWith('/');
  });

  it('leaves for the home page when nothing asked for a screen', async () => {
    const { sessions, assign, type, submit } = await render();
    sessions.signIn.mockResolvedValue({ kind: 'signed-in', session: staff });

    await type('email', 'ada@6311arts.test');
    await type('password', 'pw');
    await submit();

    expect(assign).toHaveBeenCalledWith('/');
  });

  it('keeps a member account out of the CMS instead of sending it to a 403', async () => {
    const { element, sessions, assign, type, submit } = await render({ returnUrl: '/admin' });
    sessions.signIn.mockResolvedValue({
      kind: 'signed-in',
      session: { displayName: 'Bo', roles: ['Member'] },
    });

    await type('email', 'bo@example.test');
    await type('password', 'pw');
    await submit();

    expect(assign).not.toHaveBeenCalled();
    expect(element.textContent).toContain('This account can’t use the CMS');
  });

  it.each<[SignInResult, string]>([
    [{ kind: 'refused' }, 'Check your email address and password, then try again.'],
    [{ kind: 'rate-limited', retryAfterSeconds: 240 }, 'Wait 4 minutes before trying again.'],
    [{ kind: 'rate-limited', retryAfterSeconds: null }, 'Wait a few minutes before trying again.'],
    [{ kind: 'unavailable' }, 'Try again shortly.'],
  ])('explains %j, clears the password and puts focus back on it', async (result, message) => {
    const { element, sessions, assign, type, submit, input } = await render();
    sessions.signIn.mockResolvedValue(result);

    await type('email', 'ada@6311arts.test');
    await type('password', 'wrong');
    await submit();

    expect(assign).not.toHaveBeenCalled();
    expect(element.querySelector('ui-notice[role="alert"]')?.textContent).toContain(message);
    expect(input('password').value).toBe('');
    expect(document.activeElement).toBe(input('password'));
  });

  it('survives a repeated returnUrl and falls back to the home page', async () => {
    const { sessions, assign, type, submit } = await render({
      returnUrl: ['/admin/x', '//evil.example'] as unknown as string,
    });
    sessions.signIn.mockResolvedValue({ kind: 'signed-in', session: staff });

    await type('email', 'ada@6311arts.test');
    await type('password', 'pw');
    await submit();

    expect(assign).toHaveBeenCalledWith('/');
  });

  it('keeps its live region in the page before anything is said', async () => {
    const { element } = await render();
    const region = element.querySelector('.account-panel__status');

    expect(region?.getAttribute('aria-live')).toBe('polite');
    expect(region?.querySelector('ui-notice')).toBeNull();
  });

  it('offers a way back in for a forgotten password', async () => {
    const { element } = await render({ returnUrl: '/admin/media' });
    const link = element.querySelector<HTMLAnchorElement>('a[href="/account/forgot-password"]');

    expect(link?.textContent?.trim()).toBe('Forgot your password?');
  });

  it.each([
    ['password-changed', 'Your password has been changed'],
    ['email-confirmed', 'Your email address is confirmed'],
  ])('says why a finished %s link sent the person here', async (notice, heading) => {
    const { element } = await render({ arrivedWith: notice });

    expect(element.querySelector('ui-notice[role="status"]')?.textContent).toContain(heading);
    // Announced as a change to the live region, and read in context from the heading.
    expect(document.activeElement).toBe(element.querySelector('h1'));
  });

  it('ignores a notice it does not know', async () => {
    const { element } = await render({ arrivedWith: '<img src=x>' });

    expect(element.querySelector('ui-notice')).toBeNull();
  });

  it('offers to continue when a staff session already exists', async () => {
    const { element } = await render({ current: staff, returnUrl: '/admin/media' });

    expect(element.textContent).toContain('You’re signed in as Ada.');
    expect(element.querySelector('ui-notice a')?.getAttribute('href')).toBe('/admin/media');
  });
});
