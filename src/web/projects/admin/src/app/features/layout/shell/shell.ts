import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { UiButton, UiIcon } from '@6311/ui';
import { STAFF_ROLES } from '../../../models/session';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { UnsavedChanges } from '../../../core/guards/unsaved-changes.guard';
import { SessionService } from '../../../core/session/session.service';
import { CMS_SECTIONS } from '../sections';

/**
 * The CMS frame: a charcoal top bar with who is signed in, the section nav, and the routed screen. Below 900px the
 * nav folds behind a "Sections" button so the screen keeps the width.
 */
@Component({
  selector: 'cms-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, UiButton, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
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
