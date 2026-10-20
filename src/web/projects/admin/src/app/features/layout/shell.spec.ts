import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BrowserNavigation } from '../../core/browser/browser-navigation';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { SessionService } from '../../core/session/session.service';
import { CMS_SECTIONS } from './sections';
import { Shell } from './shell';

@Component({ template: '<p>screen</p>' })
class Screen {}

async function render(roles: string[]) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([
        {
          path: '',
          component: Shell,
          children: CMS_SECTIONS.map((section) => ({ path: section.path, component: Screen })),
        },
      ]),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const session = TestBed.inject(SessionService);
  const assign = vi
    .spyOn(TestBed.inject(BrowserNavigation), 'assign')
    .mockImplementation(() => undefined);

  const loaded = session.ensureLoaded();
  http.expectOne('/bff/session').flush({ displayName: 'Ada Lovelace', roles });
  await loaded;

  const harness = await RouterTestingHarness.create('/classes');
  const element = harness.routeNativeElement?.closest('cms-shell') as HTMLElement;
  return {
    harness,
    element: element ?? (harness.fixture.nativeElement as HTMLElement),
    http,
    assign,
  };
}

const navLabels = (element: HTMLElement) =>
  [...element.querySelectorAll('nav[aria-label="CMS sections"] a')].map((a) =>
    a.textContent?.trim(),
  );

describe('Shell', () => {
  it('lists every section for an Admin, in order', async () => {
    const { element } = await render(['Admin']);

    expect(navLabels(element)).toEqual([
      'Dashboard',
      'Classes',
      'Exhibitions',
      'Artists',
      'Shop',
      'Memberships',
      'Pages',
      'Media',
      'Forms',
      'Redirects',
      'Sync',
    ]);
  });

  it('hides Sync from an Editor', async () => {
    const { element } = await render(['Editor']);

    expect(navLabels(element)).not.toContain('Sync');
    expect(navLabels(element)).toContain('Classes');
  });

  it('marks the current section for assistive technology', async () => {
    const { element } = await render(['Editor']);

    const current = element.querySelector('a[aria-current="page"]');
    expect(current?.textContent?.trim()).toBe('Classes');
  });

  it('says who is signed in, with the role as a word', async () => {
    const { element } = await render(['Editor', 'Member']);

    const user = element.querySelector('.bar__user')?.textContent?.replace(/\s+/g, ' ').trim();
    expect(user).toBe('Signed in as Ada Lovelace Editor');
  });

  it('folds the nav behind a button that reports its state', async () => {
    const { element, harness } = await render(['Admin']);
    const toggle = element.querySelector<HTMLButtonElement>('button[aria-controls="cms-nav"]')!;

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    harness.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector('#cms-nav')?.classList).toContain('nav--open');
  });

  it('signs out through the BFF', async () => {
    const { element, harness, http, assign } = await render(['Admin']);
    const button = [...element.querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === 'Sign out',
    )!;

    button.click();
    harness.detectChanges();
    http.expectOne('/bff/logout').flush(null, { status: 204, statusText: 'No Content' });
    await harness.fixture.whenStable();

    expect(assign).toHaveBeenCalledWith('/signin');
  });

  it('asks before signing out everywhere, and does nothing on "stay signed in"', async () => {
    const { element, harness, http, assign } = await render(['Admin']);
    const confirm = TestBed.inject(ConfirmService);
    const button = () =>
      [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Sign out everywhere')!;

    button().click();
    expect(confirm.request()?.heading).toBe('Sign out everywhere?');
    confirm.answer(false);
    await harness.fixture.whenStable();
    http.expectNone('/bff/logout-everywhere');

    button().click();
    confirm.answer(true);
    await harness.fixture.whenStable();
    http.expectOne('/bff/logout-everywhere').flush(null, { status: 204, statusText: 'No Content' });
    await harness.fixture.whenStable();
    expect(assign).toHaveBeenCalledWith('/signin');
  });

  it('moves focus to the screen when a section is chosen from the folded nav', async () => {
    const { element, harness } = await render(['Admin']);
    element.querySelector<HTMLButtonElement>('button[aria-controls="cms-nav"]')!.click();
    harness.detectChanges();

    await harness.navigateByUrl('/media');

    expect(element.querySelector('#cms-nav')?.classList).not.toContain('nav--open');
    expect(document.activeElement?.id).toBe('main');
  });

  it('has a skip link to the main region', async () => {
    const { element } = await render(['Admin']);

    expect(element.querySelector('a.skip')?.getAttribute('href')).toBe('#main');
    expect(element.querySelector('main#main')).not.toBeNull();
  });
});
