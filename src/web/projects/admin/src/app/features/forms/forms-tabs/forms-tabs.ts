import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { GALLERY_TIME_ZONE } from '../../../core/time/gallery-time';

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
  templateUrl: './forms-tabs.html',
  styleUrl: './forms-tabs.scss',
})
export class FormsTabs {
  protected readonly tabs = TABS;
}
