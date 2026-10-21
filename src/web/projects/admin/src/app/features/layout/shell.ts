import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { UiButton, UiIcon } from '@6311/ui';
import { STAFF_ROLES } from '../../models/session';
import { ConfirmService } from '../../core/confirm/confirm.service';
import { UnsavedChanges } from '../../core/guards/unsaved-changes.guard';
import { SessionService } from '../../core/session/session.service';
import { CMS_SECTIONS } from './sections';

/**
 * The CMS frame: a charcoal top bar with who is signed in, the section nav, and the routed screen. Below 900px the
 * nav folds behind a "Sections" button so the screen keeps the width.
 */
@Component({
  selector: 'cms-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, UiButton, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip" href="#main" (click)="skipToMain($event)">Skip to content</a>

    <header class="bar">
      <a class="bar__brand" routerLink="/">
        <span class="bar__name">6311arts</span>
        <span class="bar__tag">CMS</span>
      </a>

      <button
        uiButton
        type="button"
        variant="border"
        class="bar__toggle"
        [attr.aria-expanded]="navOpen()"
        aria-controls="cms-nav"
        (click)="navOpen.set(!navOpen())"
      >
        <ui-icon [name]="navOpen() ? 'close' : 'menu'" [size]="20" [stroke]="2" />
        Sections
      </button>

      @if (session.session(); as who) {
        <div class="bar__who">
          <span class="bar__user">
            <span class="sr-only">Signed in as </span>{{ who.displayName }}
            <span class="bar__role">{{ roleLabel() }}</span>
          </span>
          <button uiButton type="button" variant="border" [busy]="leaving()" (click)="signOut()">
            Sign out
          </button>
          <button
            uiButton
            type="button"
            variant="border"
            [busy]="leaving()"
            (click)="signOutEverywhere()"
          >
            Sign out everywhere
          </button>
        </div>
      }
    </header>

    <div class="frame">
      <nav id="cms-nav" class="nav" aria-label="CMS sections" [class.nav--open]="navOpen()">
        <ul>
          @for (section of sections(); track section.path) {
            <li>
              <a
                [routerLink]="'/' + section.path"
                routerLinkActive="nav__link--active"
                ariaCurrentWhenActive="page"
                [routerLinkActiveOptions]="{ exact: section.path === '' }"
                class="nav__link"
              >
                <ui-icon [name]="section.icon" [size]="20" [stroke]="1.6" />
                {{ section.label }}
              </a>
            </li>
          }
        </ul>
      </nav>

      <main id="main" class="main" tabindex="-1">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
      background: var(--light);
    }
    .bar {
      --focus: var(--focus-on-dark);
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-4);
      padding: var(--space-3) var(--space-5);
      background: var(--ink);
      color: var(--white);
    }
    .bar__brand {
      display: inline-flex;
      align-items: baseline;
      gap: var(--space-2);
      color: var(--white);
      text-decoration: none;
    }
    .bar__name {
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-wide);
      text-transform: uppercase;
    }
    .bar__tag {
      font-size: var(--fs-label);
      font-weight: var(--fw-bold);
      letter-spacing: var(--track-label);
      color: var(--white-70);
    }
    .bar__toggle {
      display: none;
    }
    .bar__who {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      margin-left: auto;
    }
    .bar__user {
      font-size: var(--fs-meta);
    }
    .bar__role {
      margin-left: var(--space-2);
      font-size: var(--fs-label);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
      color: var(--white-70);
    }
    .frame {
      display: grid;
      grid-template-columns: 220px minmax(0, 1fr);
      flex: 1;
    }
    .nav {
      background: var(--white);
      border-right: var(--border-thin) solid var(--line);
      padding: var(--space-4) 0;
    }
    .nav ul {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .nav__link {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-height: 40px;
      padding: var(--space-3) var(--space-5);
      border-left: var(--border-accent) solid transparent;
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: var(--fw-bold);
      letter-spacing: var(--track-button);
      text-transform: uppercase;
      text-decoration: none;
    }
    .nav__link:hover {
      background: var(--light);
      color: var(--ink);
    }
    .nav__link--active {
      border-left-color: var(--ink);
      background: var(--light);
      color: var(--ink);
    }
    .main {
      padding: var(--space-6) var(--pad);
      outline: none;
    }
    @media (max-width: 900px) {
      .bar__toggle {
        display: inline-flex;
      }
      .frame {
        grid-template-columns: minmax(0, 1fr);
      }
      .nav {
        display: none;
        border-right: 0;
        border-bottom: var(--border-thin) solid var(--line);
      }
      .nav--open {
        display: block;
      }
    }
  `,
})
export class Shell {
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly confirm = inject(ConfirmService);
  private readonly unsaved = inject(UnsavedChanges);

  protected readonly navOpen = signal(false);
  protected readonly leaving = signal(false);

  /** Only the sections this person may open: a link to a refusal is noise. */
  protected readonly sections = computed(() =>
    CMS_SECTIONS.filter((section) => this.session.hasAnyRole(section.roles)),
  );

  /** The highest staff role, as a word. Status is never colour alone, and here it is only a word. */
  protected readonly roleLabel = computed(() => {
    const roles = this.session.session()?.roles ?? [];
    return STAFF_ROLES.find((role) => roles.includes(role)) ?? '';
  });

  constructor() {
    // On a narrow screen, choosing a section folds the nav away again.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        // The folded nav hides the link that had focus, so focus moves to the screen that just opened rather
        // than dropping to the page.
        if (!this.navOpen()) return;
        this.navOpen.set(false);
        this.document.getElementById('main')?.focus();
      });
  }

  /**
   * Signing out leaves the app, which the router's unsaved-changes guard never sees. So unsaved work is asked about
   * here, before the session ends: after the BFF has signed out, "Keep editing" would leave a screen that can't save.
   */
  protected async signOut(): Promise<void> {
    if (this.unsaved.any() && !(await this.unsaved.confirmLeavingApp())) return;
    await this.leave(() => this.session.signOut());
  }

  protected async signOutEverywhere(): Promise<void> {
    const unsaved = this.unsaved.any();
    const confirmed = await this.confirm.confirm({
      heading: 'Sign out everywhere?',
      message:
        'This ends your sessions on every device and browser, including this one. Others end within five minutes.' +
        (unsaved ? ' Your unsaved changes on this screen will be lost.' : ''),
      confirmLabel: 'Sign out everywhere',
      cancelLabel: 'Stay signed in',
    });
    if (!confirmed) return;
    if (unsaved) this.unsaved.release();
    await this.leave(() => this.session.signOutEverywhere());
  }

  /** The skip link moves focus as well as scroll, so keyboard users land in the screen, not just near it. */
  protected skipToMain(event: Event): void {
    event.preventDefault();
    this.document.getElementById('main')?.focus();
  }

  private async leave(action: () => Promise<void>): Promise<void> {
    this.leaving.set(true);
    try {
      await action();
    } catch {
      // The problem interceptor has already said why; stay signed in and on this screen, its work protected again.
      this.leaving.set(false);
      this.unsaved.hold();
    }
  }
}
