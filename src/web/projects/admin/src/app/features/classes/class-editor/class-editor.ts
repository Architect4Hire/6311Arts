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
import { SessionService } from '../../../core/session/session.service';
import { STATUS_CHIPS, isSlug, slugFromName } from '../../../models/content';
import {
  CLASS_CODES,
  CLASS_LIMITS,
  ClassCommand,
  ClassCreate,
  ClassOffering,
  ClassProblem,
  ClassUpdate,
  decodeClassProblem,
} from '../../../models/class';
import { MediaAsset, MediaAssetSummary } from '../../../models/media';
import { ROLES } from '../../../models/session';
import { ClassService } from '../../../services/class.service';
import { MediaService } from '../../../services/media.service';
import { PreviewLinkButton } from '../../../shared/preview-link/preview-link';
import { RichTextEditor } from '../../../shared/rich-text-editor/rich-text-editor';
import { ScheduleControl } from '../../../shared/schedule-control/schedule-control';
import { MediaPicker } from '../../media/media-picker/media-picker';
import {
  ClassRuns,
  RunDraft,
  newRunDraft,
  runDraftsOf,
  runErrorOf,
  runInputsOf,
  runName,
  runsEqual,
} from '../run-list/run-list';

interface Draft {
  readonly title: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the title (new classes only). */
  readonly slugEdited: boolean;
  readonly summary: string;
  /** HTML, as the rich-text editor holds it. */
  readonly body: string;
  readonly classType: string;
  readonly level: string;
  readonly heroMediaId: string | null;
  /** The terms it is offered in, each with its instructor, sessions, seats and price (ADR 0044). */
  readonly runs: readonly RunDraft[];
}

/** What the editor shows of the hero: a thumbnail and whether it has the alt text publishing needs. */
interface Hero {
  readonly id: string;
  readonly url: string | null;
  readonly altText: string | null;
  /** The image's details couldn't be fetched: its alt text is unknown, not missing. */
  readonly unavailable?: boolean;
}

type Status =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'commanding'; readonly command: ClassCommand }
  | { readonly kind: 'deleting' }
  | { readonly kind: 'conflict'; readonly theirs: ClassOffering };

type FieldName =
  'title' | 'slug' | 'summary' | 'body' | 'classType' | 'level' | 'heroMediaId' | 'runs';

type FieldErrors = Partial<Record<FieldName, string>>;

const EMPTY: Draft = {
  title: '',
  slug: '',
  slugEdited: false,
  summary: '',
  body: '',
  classType: '',
  level: '',
  heroMediaId: null,
  runs: [],
};

/** Router state set by "Create class", read by the editor it navigates to. */
const CREATED_STATE = 'classCreated';

const COMMAND_LABELS: Readonly<
  Record<ClassCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The class is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
  schedule: { busy: 'Scheduling…', done: 'Scheduled.' },
  unschedule: { busy: 'Unscheduling…', done: 'Unscheduled. The class is a draft again.' },
};

/**
 * One class (`/admin/classes/new`, `/admin/classes/:id`): its copy, its hero image, its runs, and then publish,
 * schedule, unpublish or (Admins) archive.
 *
 * **Runs** (ADR 0044). A class is offered in runs — the same class over different terms, each with its own
 * instructor (chosen from the Instructors list), sessions, seats, SKU and price. A seat is bought in a run.
 *
 * **What the CMS owns and what Square does** (square.md, ADR 0032, 0037). The copy, each run's instructor, sessions,
 * SKU and seat count, and its starting price are the CMS's. A run's starting price is an ordinary field only until
 * Square reports a price for it: after that it is shown as Square's, with the change-price command arriving in 5.4a.
 * The seats left and sold-out come from Square and are never edited here.
 *
 * Unsaved edits survive every recoverable failure, and a 409 shows the saved version beside the editor's own
 * (frontend.md). Status commands act on the saved version, so they wait until the form is saved.
 */
@Component({
  selector: 'cms-class-editor',
  imports: [
    FormsModule,
    RouterLink,
    MediaPicker,
    PreviewLinkButton,
    RichTextEditor,
    ScheduleControl,
    ClassRuns,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './class-editor.html',
  styleUrl: './class-editor.scss',
})
export class ClassEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/classes/new`. */
  readonly id = input<string>();

  private readonly classes = inject(ClassService);
  private readonly media = inject(MediaService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');
  private readonly bodyEditor = viewChild(RichTextEditor);

  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = CLASS_LIMITS;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The class as saved; `null` while creating one. */
  protected readonly offering = signal<ClassOffering | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly hero = signal<Hero | null>(null);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');
  protected readonly picking = signal(false);

  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || kind === 'commanding' || kind === 'deleting';
  });
  protected readonly commanding = computed(() => {
    const status = this.status();
    return status.kind === 'commanding' ? status.command : null;
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });
  protected readonly slugLocked = computed(() => this.offering()?.slugLocked ?? false);
  protected readonly canPublish = computed(() => {
    const status = this.offering()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.offering()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

  protected readonly deleting = computed(() => this.status().kind === 'deleting');

  /** A saved class an Admin can delete, whatever its status (ADR 0052). */
  protected readonly canDelete = computed(() => this.isAdmin() && this.offering() !== null);

  /** The currency the API returns amounts in: every run's is the same. */
  protected readonly currency = computed(() => this.offering()?.runs[0]?.currency ?? 'USD');

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.offering() ? 'Saving…' : 'Creating…';
    return this.offering() ? 'Save' : 'Create class';
  });

  /**
   * Once published, the address is someone's bookmark: changing it is allowed, and the old address is redirected to
   * the new one when it is saved (ADR 0035). The hint says so, and names the address that will redirect.
   */
  protected readonly slugHint = computed(() => {
    const saved = this.offering();
    const slug = this.draft().slug;
    if (!this.slugLocked() || !saved) {
      return `6311arts.com/classes/${slug || '…'} — lower-case letters, digits and hyphens.`;
    }
    return slug === saved.slug
      ? `6311arts.com/classes/${slug} — published, so changing it sends visitors from this address to the new one.`
      : `6311arts.com/classes/${slug || '…'} — on saving, visitors to /classes/${saved.slug} are sent here.`;
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const offering = this.offering();
    if (!offering) {
      // A new class starts with one empty run, which is not an edit until something is typed into it.
      return (
        draft.title.trim() !== '' ||
        draft.summary.trim() !== '' ||
        draft.body !== '' ||
        draft.runs.some(
          (run) =>
            run.label.trim() !== '' ||
            run.instructor !== null ||
            run.cadence.trim() !== '' ||
            run.capacity.trim() !== '' ||
            run.price.trim() !== '' ||
            run.sessions.some((row) => row.date || row.starts || row.ends),
        )
      );
    }

    // Asked structurally rather than from what would be sent: a run that is still half filled in is a change the
    // person can lose, and it is `save` that refuses it with a message.
    return runsChanged(offering, draft) || Object.keys(changesOf(offering, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.id();
      untracked(() => void this.load());
    });

    // The hero's thumbnail and alt text follow whichever image the draft names.
    effect(() => {
      const heroId = this.draft().heroMediaId;
      untracked(() => void this.showHero(heroId));
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
      this.offering.set(null);
      this.draft.set({ ...EMPTY, runs: [newRunDraft([], null)] });
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const offering = await firstValueFrom(this.classes.get(id));
      this.offering.set(offering);
      this.draft.set(draftOf(offering));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Class created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === CLASS_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
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

  protected setTitle(title: string): void {
    const followsTitle = !this.offering() && !this.draft().slugEdited;
    this.patch({ title, ...(followsTitle ? { slug: slugFromName(title) } : {}) });
  }

  protected setSlug(slug: string): void {
    this.patch({ slug, slugEdited: true });
  }

  protected setRuns(runs: readonly RunDraft[]): void {
    this.patch({ runs });
  }

  protected chooseHero(choice: MediaAssetSummary): void {
    this.hero.set({ id: choice.id, url: choice.thumbnailUrl, altText: choice.altText });
    this.patch({ heroMediaId: choice.id });
  }

  protected removeHero(): void {
    this.patch({ heroMediaId: null });
  }

  protected retryHero(): void {
    void this.showHero(this.draft().heroMediaId, true);
  }

  protected discard(): void {
    const offering = this.offering();
    if (offering) this.draft.set(draftOf(offering));
    this.errors.set({});
    this.formError.set(null);
  }

  /** The runs in a line each comparison cell can hold: who teaches each, from when, and for how many sittings. */
  protected describeRuns(runs: readonly RunDraft[]): string {
    if (runs.length === 0) return 'None';
    return runs
      .map((run, index) => {
        const first = run.sessions[0];
        const when = first?.date ? ` from ${first.date}` : '';
        return `${runName(run, index)}: ${run.instructor?.name ?? 'no instructor'}${when}, ${run.sessions.length} session${run.sessions.length === 1 ? '' : 's'}`;
      })
      .join('; ');
  }

  protected savedRuns(offering: ClassOffering): string {
    return this.describeRuns(runDraftsOf(offering.runs));
  }

  protected statusText(offering: ClassOffering): string {
    switch (offering.status) {
      case 'Published':
        return 'Published: on the public site. Edits show there within a minute.';
      case 'Archived':
        return 'Archived: retired from the site. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return offering.publishedAt
          ? 'A draft again, after being published. Its web address stays as it is.'
          : 'A draft: only staff can see it. Publishing needs a summary and at least one run, each with an instructor, a price and a session.';
    }
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = localErrors(draft);
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const offering = this.offering();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!offering) {
        const created = await firstValueFrom(this.classes.create(createOf(draft)));
        this.offering.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        await this.router.navigate(['/classes', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.classes.update(offering.id, changesOf(offering, draft), etag ?? offering.etag),
      );
      this.offering.set(saved);
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

  protected async keepMine(theirs: ClassOffering): Promise<void> {
    this.status.set({ kind: 'ready' });
    this.focus(this.heading);

    // Deliberately *not* `offering.set(theirs)` before saving. `save` diffs the draft against `offering()`, so
    // adopting their version first would make every field they changed — and this editor only loaded, never
    // touched — look like one of "my changes", and send a stale copy straight back over it. Diffing against
    // what this editor loaded sends only the fields it actually edited; their etag is what lets the write
    // through. That is what frontend.md's "a 409 keeps the editor's changes" has to mean, or keeping mine
    // silently discards theirs.
    if (!this.dirty()) {
      this.offering.set(theirs);
      this.draft.set(draftOf(theirs));
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }

    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: ClassOffering): void {
    this.offering.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this class?',
      message:
        'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this class?',
      message:
        'An archived class is retired from the site, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive class',
      cancelLabel: 'Keep it',
    });
    if (confirmed) await this.run('archive');
  }

  /**
   * Deletes the class (ADR 0052). Unlike the status commands it is offered while the form is dirty — asking
   * someone to save a record before throwing it away would be absurd — so the dialog says the unsaved edits go
   * too. It acts on the saved version's row version, as every write does.
   */
  protected async remove(): Promise<void> {
    const offering = this.offering();
    if (!offering || this.busy()) return;

    const live = offering.status === 'Published';
    const confirmed = await this.confirm.confirm({
      heading: 'Delete this class?',
      message:
        (live
          ? 'It leaves the public site within a minute, and leaves the CMS list. '
          : 'The class leaves the CMS list. ') +
        (this.dirty() ? 'Your unsaved changes go with it. ' : '') +
        'Its web address is free for another class straight away. An Admin can restore it from Deleted, as a draft. The deletion is recorded under your name.',
      confirmLabel: 'Delete class',
      cancelLabel: 'Keep it',
    });
    if (!confirmed) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'deleting' });
    try {
      await firstValueFrom(this.classes.delete(offering.id, offering.etag));

      // Back to the saved version first, so the unsaved-changes guard has nothing to ask about on the way out.
      this.draft.set(draftOf(offering));
      this.status.set({ kind: 'ready' });
      await this.router.navigate(['/classes']);
    } catch (error: unknown) {
      const problem = problemOf(error);
      this.status.set({ kind: 'ready' });
      this.showErrors(
        {},
        problem?.code === CLASS_CODES.concurrencyConflict
          ? 'Someone else changed this class a moment ago. Reload it, check it, and try again.'
          : messageOf(problem, 'The class couldn’t be deleted. Try again.'),
      );
    }
  }

  /** The `schedule` command: the only one with a body, so it does not go through {@link run} (ADR 0036). */
  protected async schedule(scheduledFor: string): Promise<void> {
    const offering = this.offering();
    if (!offering || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command: 'schedule' });
    this.announce(COMMAND_LABELS.schedule.busy);
    try {
      const updated = await firstValueFrom(
        this.classes.schedule(offering.id, scheduledFor, offering.etag),
      );
      this.offering.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS.schedule.done);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  protected async unschedule(): Promise<void> {
    await this.run('unschedule');
  }

  /** Every command but `schedule`, which carries a body and has {@link schedule} of its own (ADR 0036). */
  protected async run(command: Exclude<ClassCommand, 'schedule'>): Promise<void> {
    const offering = this.offering();
    if (!offering || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(
        this.classes.command(offering.id, command, offering.etag),
      );
      this.offering.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS[command].done);
      this.focus(this.publishingHeading);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === CLASS_CODES.concurrencyConflict && this.offering()) {
      try {
        const theirs = await firstValueFrom(this.classes.get(this.offering()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this class while you were editing. Compare the two versions.',
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
      messageOf(problem, 'The changes couldn’t be saved. Try again.'),
    );
  }

  private async commandFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === CLASS_CODES.concurrencyConflict ||
      problem?.code === CLASS_CODES.transitionInvalid;

    if (stale && this.offering()) {
      try {
        const latest = await firstValueFrom(this.classes.get(this.offering()!.id));
        this.offering.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === CLASS_CODES.concurrencyConflict
        ? 'Someone else changed this class a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The first invalid field takes focus, so its message is read; the description's is the rich text's own.
    afterNextRender(
      () => {
        const invalid = this.host.nativeElement.querySelector<HTMLElement>(
          'input[aria-invalid="true"], textarea[aria-invalid="true"]',
        );
        if (invalid) invalid.focus();
        else if (errors.body) this.bodyEditor()?.focus();
      },
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }

  private async showHero(id: string | null, retry = false): Promise<void> {
    if (id === null) {
      this.hero.set(null);
      return;
    }
    if (!retry && this.hero()?.id === id) return;

    this.hero.set(null);
    try {
      const asset = await firstValueFrom(this.media.get(id));
      if (this.draft().heroMediaId === id) this.hero.set(heroOf(asset));
    } catch {
      if (this.draft().heroMediaId === id) {
        this.hero.set({ id, url: null, altText: null, unavailable: true });
      }
    }
  }
}

function draftOf(offering: ClassOffering): Draft {
  return {
    title: offering.title,
    slug: offering.slug,
    slugEdited: true,
    summary: offering.summary ?? '',
    body: offering.body ?? '',
    classType: offering.classType ?? '',
    level: offering.level ?? '',
    heroMediaId: offering.heroMediaId,
    runs: runDraftsOf(offering.runs),
  };
}

function createOf(draft: Draft): ClassCreate {
  return {
    title: draft.title.trim(),
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(draft.summary.trim() ? { summary: draft.summary.trim() } : {}),
    ...(draft.body ? { body: draft.body } : {}),
    ...(draft.classType.trim() ? { classType: draft.classType.trim() } : {}),
    ...(draft.level.trim() ? { level: draft.level.trim() } : {}),
    ...(draft.heroMediaId ? { heroMediaId: draft.heroMediaId } : {}),
    // Unreachable with an incomplete run: `localErrors` refuses one before anything is sent.
    ...(draft.runs.length > 0 ? { runs: runInputsOf(draft.runs) ?? [] } : {}),
  };
}

/**
 * Only what changed; an emptied text field is sent as "" so the API clears it. A run's price, seats left and
 * sold-out are never here: they are Square's record (ADR 0032).
 */
function changesOf(offering: ClassOffering, draft: Draft): ClassUpdate {
  const title = draft.title.trim();
  const summary = draft.summary.trim();
  const classType = draft.classType.trim();
  const level = draft.level.trim();
  const runs = runInputsOf(draft.runs);

  return {
    ...(title !== offering.title ? { title } : {}),
    ...(draft.slug !== offering.slug ? { slug: draft.slug } : {}),
    ...(summary !== (offering.summary ?? '') ? { summary } : {}),
    ...(draft.body !== (offering.body ?? '') ? { body: draft.body } : {}),
    ...(classType !== (offering.classType ?? '') ? { classType } : {}),
    ...(level !== (offering.level ?? '') ? { level } : {}),
    ...(draft.heroMediaId !== offering.heroMediaId
      ? draft.heroMediaId === null
        ? { removeHero: true as const }
        : { heroMediaId: draft.heroMediaId }
      : {}),
    ...(runs !== null && runsChanged(offering, draft) ? { runs } : {}),
  };
}

/** Whether the runs say something different from the saved ones, whether or not they can be sent yet. */
function runsChanged(offering: ClassOffering, draft: Draft): boolean {
  return !runsEqual(draft.runs, runDraftsOf(offering.runs));
}

function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
  if (draft.title.trim() === '') errors.title = 'Enter the class’s title.';
  if (!isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example intro-to-screen-printing).'
      : 'Enter a web address, for example intro-to-screen-printing.';
  }
  const runError = runErrorOf(draft.runs);
  if (runError) errors.runs = runError;
  return errors;
}

function heroOf(asset: MediaAsset): Hero {
  const url =
    asset.renditions.find((rendition) => rendition.name === '800.webp')?.url ??
    asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ??
    [...asset.renditions].sort((a, b) => a.width - b.width)[0]?.url ??
    null;
  return { id: asset.id, url, altText: asset.altText };
}

function problemOf(error: unknown): ClassProblem | null {
  return error instanceof HttpErrorResponse ? decodeClassProblem(error.error, error.status) : null;
}

function fieldErrorsOf(problem: ClassProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  const keys: readonly FieldName[] = [
    'title',
    'slug',
    'summary',
    'body',
    'classType',
    'level',
    'heroMediaId',
    'runs',
  ];
  for (const key of keys) {
    const message = problem?.fieldErrors[key]?.[0];
    if (message) errors[key] = message;
  }
  return errors;
}

function messageOf(problem: ClassProblem | null, fallback: string): string {
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
