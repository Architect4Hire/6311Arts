import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BffSessionService, Session } from '../../../core/bff-session.service';
import { StaffAccess } from './staff-access';

class FakeSessions {
  current = vi.fn<() => Promise<Session | null>>(async () => null);
}

async function render(current: Session | null) {
  const sessions = new FakeSessions();
  sessions.current.mockResolvedValue(current);

  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([]),
      { provide: BffSessionService, useValue: sessions },
    ],
  });

  const fixture: ComponentFixture<StaffAccess> = TestBed.createComponent(StaffAccess);
  await fixture.whenStable();

  return { fixture, sessions, element: fixture.nativeElement as HTMLElement };
}

const staff: Session = { displayName: '6311arts Admin', roles: ['Admin'] };
const editor: Session = { displayName: 'Ada', roles: ['Editor'] };
const member: Session = { displayName: 'Bo', roles: ['Member'] };

describe('StaffAccess', () => {
  it('offers a visitor the sign-in page', async () => {
    const { element } = await render(null);

    const link = element.querySelector('a')!;
    expect(link.textContent?.trim()).toBe('Staff sign-in');
    expect(link.getAttribute('href')).toBe('/signin');
  });

  it('sends staff to the CMS in a new tab, without a handle on this one', async () => {
    const { element } = await render(staff);

    const link = element.querySelector('a')!;
    expect(link.getAttribute('href')).toBe('/admin/');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener');
    expect(link.textContent).toContain('Open the CMS');
  });

  it('says a new tab is coming, for anyone not watching it happen', async () => {
    const { element } = await render(editor);

    expect(element.querySelector('a .sr-only')?.textContent).toContain('opens in a new tab');
  });

  it('names who is signed in', async () => {
    const { element } = await render(staff);

    expect(element.textContent).toContain('6311arts Admin');
  });

  it('offers a member neither the CMS nor a sign-in link', async () => {
    const { element } = await render(member);

    expect(element.querySelector('a')).toBeNull();
    expect(element.textContent).not.toContain('Bo');
  });

  // The footer is part of the SSR shell, which is cached with s-maxage: a name or a CMS link rendered there would
  // be served to the next visitor from a shared cache.
  it('renders nothing until the session has answered', async () => {
    const sessions = new FakeSessions();
    let answer!: (session: Session | null) => void;
    sessions.current.mockReturnValue(new Promise((resolve) => (answer = resolve)));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: BffSessionService, useValue: sessions },
      ],
    });
    const fixture = TestBed.createComponent(StaffAccess);
    await fixture.whenStable();

    // Asked, still unanswered: the footer shows no name, no CMS link and no sign-in link.
    expect(sessions.current).toHaveBeenCalledTimes(1);
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');

    answer(staff);
    // The component is awaiting that promise, so let its continuation run before asking what it rendered.
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Open the CMS');
  });

  it('asks the gateway once per page, not once per render', async () => {
    const { fixture, sessions } = await render(staff);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(sessions.current).toHaveBeenCalledTimes(1);
  });
});
