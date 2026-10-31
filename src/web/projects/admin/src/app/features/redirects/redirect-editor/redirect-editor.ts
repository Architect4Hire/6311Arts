import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField } from '@6311/ui';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { messageOf, problemOf } from '../../../core/http/problem-message';
import { CONTENT_CODES } from '../../../models/content';
import {
  REDIRECT_LIMITS,
  Redirect,
  RedirectCreate,
  RedirectUpdate,
  SOURCE_CHIPS,
} from '../../../models/redirect';
import { RedirectService } from '../../../services/redirect.service';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import {
  CompareRow,
  EditorChip,
  FieldErrors,
  RecordEditor,
  orDash,
} from '../../../shared/record-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';

interface Draft {
  readonly fromPath: string;
  readonly toPath: string;
  readonly statusCode: number;
}

const FIELDS = ['fromPath', 'toPath', 'statusCode'] as const;
type Field = (typeof FIELDS)[number];

const EMPTY: Draft = { fromPath: '', toPath: '', statusCode: 301 };

/**
 * One redirect (`/admin/redirects/new`, `/admin/redirects/:id`, Admins only): the old address, where it leads, and
 * whether for good or for now; then delete it. The API refuses loops and chains longer than one hop, and the reason
 * shows beside the field it is about. A redirect has no lifecycle: it is live once written (ADR 0035).
 */
@Component({
  selector: 'cms-redirect-editor',
  imports: [FormsModule, UiButton, UiControl, UiField, EditorFrame, SaveActions],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './redirect-editor.html',
  styleUrl: './redirect-editor.scss',
})
export class RedirectEditor extends RecordEditor<
  Redirect,
  Draft,
  RedirectCreate,
  RedirectUpdate,
  Field
> {
  private readonly redirects = inject(RedirectService);
  private readonly confirm = inject(ConfirmService);

  protected readonly limits = REDIRECT_LIMITS;
  protected readonly redirect = this.record;
  protected readonly deleting = signal(false);

  constructor() {
    super({
      api: inject(RedirectService),
      noun: 'redirect',
      plural: 'redirects',
      route: '/redirects',
      label: (redirect) => redirect.fromPath,
      createdMessage: 'Redirect created.',
      createdState: 'redirectCreated',
      empty: () => EMPTY,
      draftOf: (redirect) => ({
        fromPath: redirect.fromPath,
        toPath: redirect.toPath,
        statusCode: redirect.statusCode,
      }),
      createOf: (draft) => ({
        fromPath: draft.fromPath.trim(),
        toPath: draft.toPath.trim(),
        statusCode: draft.statusCode,
      }),
      changesOf,
      started: (draft) => draft.fromPath.trim() !== '' || draft.toPath.trim() !== '',
      localErrors,
      fields: FIELDS,
    });
  }

  protected async remove(): Promise<void> {
    const redirect = this.redirect();
    if (!redirect || this.busy()) return;

    const confirmed = await this.confirm.confirm({
      heading: 'Delete this redirect?',
      message: `Visitors to ${redirect.fromPath} will no longer be sent anywhere. The deletion is recorded under your name.`,
      confirmLabel: 'Delete redirect',
      cancelLabel: 'Keep it',
    });
    if (!confirmed) return;

    this.deleting.set(true);
    try {
      await firstValueFrom(this.redirects.delete(redirect.id, redirect.etag));
      this.show(redirect);
      this.deleting.set(false);
      await this.router.navigate(['/redirects']);
    } catch (error: unknown) {
      const problem = problemOf(error);
      this.deleting.set(false);
      this.showErrors(
        {},
        problem?.code === CONTENT_CODES.concurrencyConflict
          ? 'Someone else changed this redirect a moment ago. Reload it, check it, and try again.'
          : messageOf(problem, 'The redirect couldn’t be deleted. Try again.'),
      );
    }
  }

  protected override pending(): boolean {
    return this.deleting();
  }

  protected override chipOf(redirect: Redirect): EditorChip {
    return SOURCE_CHIPS[redirect.source];
  }

  protected override compare(theirs: Redirect, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Old address', mine: orDash(draft.fromPath), theirs: theirs.fromPath },
      { label: 'Leads to', mine: orDash(draft.toPath), theirs: theirs.toPath },
      { label: 'Type', mine: kind(draft.statusCode), theirs: kind(theirs.statusCode) },
    ];
  }
}

function kind(statusCode: number): string {
  return statusCode === 301 ? 'Permanent (301)' : 'Temporary (302)';
}

function changesOf(redirect: Redirect, draft: Draft): RedirectUpdate {
  const fromPath = draft.fromPath.trim();
  const toPath = draft.toPath.trim();
  return {
    ...(fromPath !== redirect.fromPath ? { fromPath } : {}),
    ...(toPath !== redirect.toPath ? { toPath } : {}),
    ...(draft.statusCode !== redirect.statusCode ? { statusCode: draft.statusCode } : {}),
  };
}

/** What the form can tell before asking; the API normalises and checks the rest. */
function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
  if (!draft.fromPath.trim().startsWith('/'))
    errors.fromPath = 'Enter the old address, starting with / (for example /gallery).';
  const to = draft.toPath.trim();
  if (!to.startsWith('/') && !/^https:\/\//i.test(to)) {
    errors.toPath = 'Enter a page on this site starting with /, or a full https:// address.';
  }
  return errors;
}
