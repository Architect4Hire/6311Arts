import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { GALLERY_TIME_ZONE } from '../../core/time/gallery-time';

const TABS: readonly { readonly path: string; readonly label: string }[] = [
  { path: '/forms/submissions', label: 'Submissions' },
  { path: '/forms/messages', label: 'Messages' },
  { path: '/forms/newsletter', label: 'Newsletter' },
];

const RECEIVED = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: GALLERY_TIME_ZONE,
});

/** An instant as the gallery's clock reads it, for "received" and "consented" columns. */
export function formatInstant(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : RECEIVED.format(date);
}

/**
 * The Forms section's heading and its three screens (`/admin/forms/...`): the artist submissions inbox, contact
 * messages and the newsletter list. Each tab is a link to its own route, so the browser's back button, a bookmark
 * and a shared link all land on the right one; the current one says so with `aria-current="page"`.
 */
@Component({
  selector: 'cms-forms-tabs',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div>
      <h1 class="cms-title">Forms</h1>
      <p class="cms-lead">What visitors sent through the site’s forms.</p>
    </div>
    <nav class="tabs" aria-label="Forms">
      @for (tab of tabs; track tab.path) {
        <a
          class="tabs__link"
          [routerLink]="tab.path"
          routerLinkActive="tabs__link--active"
          ariaCurrentWhenActive="page"
          >{{ tab.label }}</a
        >
      }
    </nav>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }
    .cms-lead {
      margin-bottom: 0;
    }
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1) var(--space-5);
      border-bottom: var(--border-thin) solid var(--line);
    }
    .tabs__link {
      display: inline-flex;
      align-items: center;
      min-block-size: 40px;
      padding: var(--space-2) 0;
      margin-bottom: calc(-1 * var(--border-thin));
      border-bottom: var(--border) solid transparent;
      color: var(--muted);
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-decoration: none;
      text-transform: uppercase;
    }
    .tabs__link:hover {
      color: var(--ink);
    }
    .tabs__link--active {
      border-bottom-color: var(--ink);
      color: var(--ink);
    }
  `,
})
export class FormsTabs {
  protected readonly tabs = TABS;
}
