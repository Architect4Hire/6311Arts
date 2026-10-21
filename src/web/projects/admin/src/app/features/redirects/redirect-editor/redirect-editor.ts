import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { ConfirmService } from '../../../core/confirm/confirm.service';
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import {
  REDIRECT_CODES,
  REDIRECT_LIMITS,
  Redirect,
  RedirectProblem,
  RedirectUpdate,
  SOURCE_CHIPS,
  decodeRedirectProblem,
} from '../../../models/redirect';
import { RedirectService } from '../../../services/redirect.service';

interface Draft {
  readonly fromPath: string;
  readonly toPath: string;
  readonly statusCode: number;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'deleting' }
  | { readonly kind: 'conflict'; readonly theirs: Redirect };

type FieldErrors = Partial<Record<'fromPath' | 'toPath' | 'statusCode', string>>;

const EMPTY: Draft = { fromPath: '', toPath: '', statusCode: 301 };

/** Router state set by "Create redirect", read by the editor it navigates to. */
const CREATED_STATE = 'redirectCreated';

/**
 * One redirect (`/admin/redirects/new`, `/admin/redirects/:id`, Admins only): the old address, where it leads, and
 * whether for good or for now; then delete it. The API refuses loops and chains longer than one hop, and the reason
 * shows beside the field it is about.
 */
@Component({
  selector: 'cms-redirect-editor',
  imports: [FormsModule, RouterLink, UiButton, UiControl, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './redirect-editor.html',
  styleUrl: './redirect-editor.scss',
})
export class RedirectEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/redirects/new`. */
  readonly id = input<string>();

  private readonly redirects = inject(RedirectService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = SOURCE_CHIPS;
  protected readonly limits = REDIRECT_LIMITS;

  protected readonly status = signal<Status>({ kind: 'loading' });
  protected readonly redirect = signal<Redirect | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || kind === 'deleting';
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });
  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.redirect() ? 'Saving…' : 'Creating…';
    return this.redirect() ? 'Save' : 'Create redirect';
  });

  readonly dirty = computed(() => {
    const draft = this.draft();
    const redirect = this.redirect();
    if (!redirect) return draft.fromPath.trim() !== '' || draft.toPath.trim() !== '';
    return Object.keys(changesOf(redirect, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());
    effect(() => {
      this.id();
      untracked(() => void this.load());
    });
  }

  hasUnsavedChanges(): boolean {
    return this.dirty() || this.busy();
  }

  async load(): Promise<void> {
    const id = this.id();
    this.errors.set({});
    this.formError.set(null);

    if (!id) {
      this.redirect.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const redirect = await firstValueFrom(this.redirects.get(id));
      this.redirect.set(redirect);
      this.draft.set(draftOf(redirect));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Redirect created.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === REDIRECT_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected kind(statusCode: number): string {
    return statusCode === 301 ? 'Permanent (301)' : 'Temporary (302)';
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    const cleared = Object.keys(change).filter((key) => key in this.errors());
    if (cleared.length > 0) {
      this.errors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => !cleared.includes(key))),
      );
    }
  }

  protected discard(): void {
    const redirect = this.redirect();
    if (redirect) this.draft.set(draftOf(redirect));
    this.errors.set({});
    this.formError.set(null);
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = localErrors(draft);
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const redirect = this.redirect();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!redirect) {
        const created = await firstValueFrom(
          this.redirects.create({
            fromPath: draft.fromPath.trim(),
            toPath: draft.toPath.trim(),
            statusCode: draft.statusCode,
          }),
        );
        this.redirect.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        await this.router.navigate(['/redirects', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.redirects.update(redirect.id, changesOf(redirect, draft), etag ?? redirect.etag),
      );
      this.redirect.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      // Saving disabled the fieldset that held the focused control, so the browser dropped focus to <body>.
      // Put it on the heading, which is where "Saved." is announced from, rather than leaving a keyboard or
      // screen-reader user with no position on the page (WCAG 2.4.3 focus order).
      this.focus(this.heading);
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  protected async keepMine(theirs: Redirect): Promise<void> {
    this.redirect.set(theirs);
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);
    if (!this.dirty()) {
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: Redirect): void {
    this.redirect.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
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

    this.status.set({ kind: 'deleting' });
    try {
      await firstValueFrom(this.redirects.delete(redirect.id, redirect.etag));
      this.draft.set(draftOf(redirect));
      this.status.set({ kind: 'ready' });
      await this.router.navigate(['/redirects']);
    } catch (error: unknown) {
      const problem = problemOf(error);
      this.status.set({ kind: 'ready' });
      this.showErrors(
        {},
        problem?.code === REDIRECT_CODES.concurrencyConflict
          ? 'Someone else changed this redirect a moment ago. Reload it, check it, and try again.'
          : messageOf(problem, 'The redirect couldn’t be deleted. Try again.'),
      );
    }
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === REDIRECT_CODES.concurrencyConflict && this.redirect()) {
      try {
        const theirs = await firstValueFrom(this.redirects.get(this.redirect()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this redirect while you were editing. Compare the two versions.',
        );
        afterNextRender(() => this.conflictBox()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return;
      } catch {
        // Fall through to the plain message; the draft is still in the form.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      messageOf(problem, 'The redirect couldn’t be saved. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }
}

function draftOf(redirect: Redirect): Draft {
  return { fromPath: redirect.fromPath, toPath: redirect.toPath, statusCode: redirect.statusCode };
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
function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
  if (!draft.fromPath.trim().startsWith('/'))
    errors.fromPath = 'Enter the old address, starting with / (for example /gallery).';
  const to = draft.toPath.trim();
  if (!to.startsWith('/') && !/^https:\/\//i.test(to)) {
    errors.toPath = 'Enter a page on this site starting with /, or a full https:// address.';
  }
  return errors;
}

function problemOf(error: unknown): RedirectProblem | null {
  return error instanceof HttpErrorResponse
    ? decodeRedirectProblem(error.error, error.status)
    : null;
}

function fieldErrorsOf(problem: RedirectProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of ['fromPath', 'toPath', 'statusCode'] as const) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: RedirectProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0)
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
