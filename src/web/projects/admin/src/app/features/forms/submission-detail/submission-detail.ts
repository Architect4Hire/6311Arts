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
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiNotice, UiStatusChip } from '@6311/ui';
import { HasUnsavedChanges, warnBeforeUnload } from '../../../core/guards/unsaved-changes.guard';
import {
  FORMS_CODES,
  FORMS_LIMITS,
  SUBMISSION_CHIPS,
  SUBMISSION_STATUSES,
  Submission,
  SubmissionStatus,
  SubmissionUpdate,
  restockLabel,
  safeWebLink,
  wantLabel,
} from '../../../models/forms';
import { FormsService } from '../../../services/forms.service';
import { formatInstant } from '../forms-tabs/forms-tabs';
import { messageOf, problemOf } from '../../../core/http/problem-message';

interface Draft {
  readonly status: SubmissionStatus;
  readonly notes: string;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'conflict'; readonly theirs: Submission; readonly mine: Draft };

/** One answer as a row: plain text, text whose line breaks matter, or a link. Never HTML. */
interface Answer {
  readonly label: string;
  readonly text: string;
  readonly multiline?: boolean;
  /** A link target this screen built and checked: `mailto:` or an absolute `http(s)` address. */
  readonly href?: string;
  /** An address a visitor typed: opened in a new tab, with no referrer and no handle back to this one. */
  readonly external?: boolean;
}

interface AnswerGroup {
  readonly id: string;
  readonly heading: string;
  readonly answers: readonly Answer[];
}

const EMAIL = /^[^\s@<>]+@[^\s@<>]+$/;

/**
 * One artist submission (`/admin/forms/submissions/:id`): every answer, grouped as the public form asked them, the
 * pictures that came with it, and staff's status and notes. Everything shown came from an anonymous visitor, so it
 * is bound as text; their web addresses are links only when they are absolute `http(s)` ones, and open in a new
 * tab with no referrer.
 */
@Component({
  selector: 'cms-submission-detail',
  imports: [FormsModule, RouterLink, UiButton, UiControl, UiField, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submission-detail.html',
  styleUrl: './submission-detail.scss',
})
export class SubmissionDetail implements HasUnsavedChanges {
  /** The route's `:id`. */
  readonly id = input.required<string>();

  private readonly forms = inject(FormsService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly chips = SUBMISSION_CHIPS;
  protected readonly statuses = SUBMISSION_STATUSES;
  protected readonly limits = FORMS_LIMITS;

  protected readonly status = signal<Status>({ kind: 'loading' });
  protected readonly submission = signal<Submission | null>(null);
  protected readonly draft = signal<Draft>({ status: 'New', notes: '' });
  protected readonly notesError = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  protected readonly busy = computed(() => this.status().kind === 'saving');
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status : null;
  });

  readonly dirty = computed(() => {
    const submission = this.submission();
    return submission !== null && Object.keys(changesOf(submission, this.draft())).length > 0;
  });

  protected readonly groups = computed(() => {
    const submission = this.submission();
    return submission ? groupsOf(submission) : { before: [], after: [] };
  });
  protected readonly before = computed(() => this.groups().before);
  protected readonly after = computed(() => this.groups().after);

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
    this.notesError.set(null);
    this.formError.set(null);
    this.status.set({ kind: 'loading' });
    try {
      const submission = await firstValueFrom(this.forms.getSubmission(this.id()));
      this.submission.set(submission);
      this.draft.set(draftOf(submission));
      this.status.set({ kind: 'ready' });
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === FORMS_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected wants(submission: Submission): string {
    return submission.wants.length === 0 ? '—' : submission.wants.map(wantLabel).join(', ');
  }

  protected instant(iso: string): string {
    return formatInstant(iso);
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    if ('notes' in change) this.notesError.set(null);
  }

  protected discard(): void {
    const submission = this.submission();
    if (submission) this.draft.set(draftOf(submission));
    this.notesError.set(null);
    this.formError.set(null);
  }

  protected async save(etag?: string): Promise<void> {
    const submission = this.submission();
    if (!submission || this.busy() || !this.dirty()) return;

    const draft = this.draft();
    if (draft.notes.trim().length > FORMS_LIMITS.notesMaxLength) {
      this.showErrors(`Keep notes under ${FORMS_LIMITS.notesMaxLength} characters.`, null);
      return;
    }

    this.notesError.set(null);
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      const saved = await firstValueFrom(
        this.forms.updateSubmission(
          submission.id,
          changesOf(submission, draft),
          etag ?? submission.etag,
        ),
      );
      this.submission.set(saved);
      this.draft.set(draftOf(saved));
      this.status.set({ kind: 'ready' });
      // Saving disabled the fieldset that held the focused control, so focus fell to <body>: put it on the
      // heading, where "Saved." is announced from (WCAG 2.4.3).
      this.focus(this.heading);
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  protected async keepMine(theirs: Submission, mine: Draft): Promise<void> {
    this.submission.set(theirs);
    this.draft.set(mine);
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);
    if (!this.dirty()) {
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: Submission): void {
    this.submission.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const submission = this.submission();

    if (problem?.code === FORMS_CODES.concurrencyConflict && submission) {
      try {
        const theirs = await firstValueFrom(this.forms.getSubmission(submission.id));
        this.status.set({
          kind: 'conflict',
          theirs,
          mine: rebased(submission, this.draft(), theirs),
        });
        this.announce(
          'Someone else saved this submission while you were reviewing it. Compare the two versions.',
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
      problem?.fieldErrors['notes']?.[0] ?? null,
      messageOf(problem, 'The submission couldn’t be saved. Try again.'),
    );
  }

  private showErrors(notesError: string | null, message: string | null): void {
    this.notesError.set(notesError);
    this.formError.set(message);
    // A form-level message is an alert notice and announces itself; only a field error alone needs saying.
    if (message === null) this.announce(notesError ?? 'Some fields need attention.');
    // Saving disabled the fieldset, so focus fell to <body>: put it on the field at fault, or else the heading.
    afterNextRender(
      () =>
        (
          this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]') ??
          this.heading()?.nativeElement
        )?.focus(),
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }
}

function draftOf(submission: Submission): Draft {
  return { status: submission.status, notes: submission.notes ?? '' };
}

/**
 * "My changes" on top of the version someone else saved: only what this editor changed from what they read is
 * theirs to keep; a field they left alone takes the saved value, so keeping mine never undoes the other edit.
 */
function rebased(read: Submission, draft: Draft, theirs: Submission): Draft {
  const changed = changesOf(read, draft);
  return {
    status: changed.status ?? theirs.status,
    notes: changed.notes ?? theirs.notes ?? '',
  };
}

function changesOf(submission: Submission, draft: Draft): SubmissionUpdate {
  const notes = draft.notes.trim();
  return {
    ...(draft.status !== submission.status ? { status: draft.status } : {}),
    // An empty string clears the notes; the API keeps null for "none".
    ...(notes !== (submission.notes ?? '') ? { notes } : {}),
  };
}

function text(label: string, value: string | null, multiline = false): Answer | null {
  return value === null || value.trim() === '' ? null : { label, text: value, multiline };
}

function count(label: string, value: number | null): Answer | null {
  return value === null ? null : { label, text: String(value) };
}

function yesNo(label: string, value: boolean | null): Answer | null {
  return value === null ? null : { label, text: value ? 'Yes' : 'No' };
}

/** A visitor's web address: a link only when it is an absolute `http(s)` URL, otherwise the text they typed. */
function web(label: string, value: string | null): Answer | null {
  if (value === null || value.trim() === '') return null;
  const href = safeWebLink(value);
  return href ? { label, text: value, href, external: true } : { label, text: value };
}

function email(value: string): Answer {
  return EMAIL.test(value)
    ? { label: 'Email', text: value, href: `mailto:${value}` }
    : { label: 'Email', text: value };
}

function group(id: string, heading: string, answers: readonly (Answer | null)[]): AnswerGroup {
  return {
    id: `submission-${id}`,
    heading,
    answers: answers.filter((answer): answer is Answer => answer !== null),
  };
}

/** The answers in the public form's order, split around the pictures (`About` … `Teaching`, then `The rest`). */
function groupsOf(submission: Submission): {
  before: readonly AnswerGroup[];
  after: readonly AnswerGroup[];
} {
  const a = submission.answers;
  const before: AnswerGroup[] = [
    group('about', 'About', [
      { label: 'Name', text: submission.name },
      email(submission.email),
      text('Pronouns', a.pronouns),
      text('Phone', a.phone),
      text('Neighborhood', a.neighborhood),
      text('ZIP code', a.zip),
      web('Website', a.website),
      a.instagram ? { label: 'Instagram', text: `@${a.instagram}` } : null,
    ]),
    group('work', 'The work', [
      text('Medium', a.medium),
      text('Other media', a.otherMedia),
      count('Years making art', a.years),
      text('Price range', a.priceBand),
      text('Bio', a.bio, true),
      text('Artist statement', a.statement, true),
    ]),
  ];
  if (a.sell) {
    before.push(
      group('sell', 'Selling', [
        count('Pieces ready', a.sell.piecesReady),
        a.sell.restock ? { label: 'Can restock', text: restockLabel(a.sell.restock) } : null,
      ]),
    );
  }
  if (a.exhibit) {
    before.push(
      group('exhibit', 'Showing', [
        text('Show title', a.exhibit.showTitle),
        count('Number of works', a.exhibit.workCount),
        text('Largest piece', a.exhibit.largest),
        text('When', a.exhibit.window),
        text('Gallery', a.exhibit.gallery),
      ]),
    );
  }
  if (a.teach) {
    before.push(
      group('teach', 'Teaching', [
        text('Class title', a.teach.classTitle),
        count('Most students', a.teach.maxStudents),
        text('Format', a.teach.format),
        text('Materials', a.teach.materials, true),
        yesNo('Has taught before', a.teach.taughtBefore),
      ]),
    );
  }

  const after: AnswerGroup[] = [
    group('rest', 'The rest', [
      web('Portfolio', a.portfolio),
      text('Heard about us', a.heard),
      { label: 'Newsletter', text: a.newsletter ? 'Yes, signed up' : 'No' },
    ]),
  ];

  return {
    before: before.map(withPlaceholder),
    after: after.map(withPlaceholder),
  };
}

function withPlaceholder(group: AnswerGroup): AnswerGroup {
  return group.answers.length > 0
    ? group
    : { ...group, answers: [{ label: 'Answers', text: 'Nothing given.' }] };
}
