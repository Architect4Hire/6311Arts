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
import { formatDateRange, instantOf, wallClockOf } from '../../../core/time/gallery-time';
import { STATUS_CHIPS, isSlug, slugFromName } from '../../../models/content';
import {
  CreditedArtist,
  EXHIBITION_CODES,
  EXHIBITION_LIMITS,
  Exhibition,
  ExhibitionCommand,
  ExhibitionCreate,
  ExhibitionProblem,
  ExhibitionReception,
  ExhibitionReceptionInput,
  ExhibitionUpdate,
  decodeExhibitionProblem,
} from '../../../models/exhibition';
import { MediaAsset, MediaAssetSummary } from '../../../models/media';
import { ROLES } from '../../../models/session';
import { ExhibitionService } from '../../../services/exhibition.service';
import { MediaService } from '../../../services/media.service';
import { MediaPicker } from '../../media/media-picker/media-picker';
import { ArtistCredits } from '../artist-credits/artist-credits';

/** A reception as the form holds it: a day and two times on the gallery's clock. */
interface ReceptionDraft {
  readonly date: string;
  readonly starts: string;
  readonly ends: string;
  readonly note: string;
}

interface Draft {
  readonly title: string;
  readonly slug: string;
  /** Whether the person has typed a slug; until then it follows the title (new exhibitions only). */
  readonly slugEdited: boolean;
  readonly summary: string;
  readonly body: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reception: ReceptionDraft | null;
  readonly heroMediaId: string | null;
  readonly artists: readonly CreditedArtist[];
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
  | { readonly kind: 'commanding'; readonly command: ExhibitionCommand }
  | { readonly kind: 'conflict'; readonly theirs: Exhibition };

const FIELDS = [
  'title',
  'slug',
  'summary',
  'body',
  'startDate',
  'endDate',
  'receptionDate',
  'receptionStarts',
  'receptionEnds',
  'receptionNote',
  'heroMediaId',
  'artistIds',
] as const;

type Field = (typeof FIELDS)[number];
type FieldErrors = Partial<Record<Field, string>>;

/** The API's field names for the reception, as the form's own. */
const API_FIELDS: Readonly<Record<string, Field>> = {
  'reception.startsAt': 'receptionStarts',
  'reception.endsAt': 'receptionEnds',
  'reception.note': 'receptionNote',
};

const EMPTY: Draft = {
  title: '',
  slug: '',
  slugEdited: false,
  summary: '',
  body: '',
  startDate: '',
  endDate: '',
  reception: null,
  heroMediaId: null,
  artists: [],
};

/** Router state set by "Create exhibition", read by the editor it navigates to. */
const CREATED_STATE = 'exhibitionCreated';

const COMMAND_LABELS: Readonly<
  Record<ExhibitionCommand, { readonly busy: string; readonly done: string }>
> = {
  publish: { busy: 'Publishing…', done: 'Published.' },
  unpublish: { busy: 'Unpublishing…', done: 'Unpublished. The exhibition is a draft again.' },
  archive: { busy: 'Archiving…', done: 'Archived.' },
  schedule: { busy: 'Scheduling…', done: 'Scheduled.' },
  unschedule: { busy: 'Unscheduling…', done: 'Unscheduled. The exhibition is a draft again.' },
};

const RECEPTION_TIME = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'America/Chicago',
});

/**
 * One exhibition (`/admin/exhibitions/new`, `/admin/exhibitions/:id`): title, web address, summary, body, when it
 * hangs, its opening reception, its hero image and the artists it credits; then publish, unpublish or (Admins)
 * archive.
 *
 * Unsaved edits survive every recoverable failure (frontend.md): a refused save keeps the form as typed, and a 409
 * shows the saved version beside the editor's own so neither is lost. Status commands act on the saved version, so
 * they wait until the form is saved. Reception times are typed and shown on the gallery's clock and travel as UTC.
 */
import { PreviewLinkButton } from '../../../shared/preview-link/preview-link';
import { ScheduleControl } from '../../../shared/schedule-control/schedule-control';

@Component({
  selector: 'cms-exhibition-editor',
  imports: [
    FormsModule,
    RouterLink,
    ArtistCredits,
    MediaPicker,
    UiButton,
    UiControl,
    UiField,
    UiNotice,
    UiStatusChip,
    PreviewLinkButton,
    ScheduleControl,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exhibition-editor.html',
  styleUrl: './exhibition-editor.scss',
})
export class ExhibitionEditor implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/exhibitions/new`. */
  readonly id = input<string>();

  private readonly exhibitions = inject(ExhibitionService);
  private readonly media = inject(MediaService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly conflictBox = viewChild<ElementRef<HTMLElement>>('conflictBox');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly publishingHeading = viewChild<ElementRef<HTMLElement>>('publishingHeading');
  private readonly heroButton = viewChild<ElementRef<HTMLElement>>('heroButton');

  /** Arrived straight from "Create exhibition", which navigated here from `exhibitions/new` (another component). */
  private readonly justCreated =
    this.router.currentNavigation()?.extras.state?.[CREATED_STATE] === true;

  protected readonly chips = STATUS_CHIPS;
  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly receptionDraftOf = receptionDraftOf;

  protected readonly status = signal<Status>({ kind: 'loading' });
  /** The exhibition as saved; `null` while creating one. */
  protected readonly exhibition = signal<Exhibition | null>(null);
  protected readonly draft = signal<Draft>(EMPTY);
  protected readonly hero = signal<Hero | null>(null);
  protected readonly errors = signal<FieldErrors>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');
  protected readonly picking = signal(false);

  protected readonly isAdmin = computed(() => this.session.hasAnyRole([ROLES.admin]));
  protected readonly busy = computed(() => {
    const kind = this.status().kind;
    return kind === 'saving' || kind === 'commanding';
  });
  protected readonly commanding = computed(() => {
    const status = this.status();
    return status.kind === 'commanding' ? status.command : null;
  });
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });
  protected readonly slugLocked = computed(() => this.exhibition()?.slugLocked ?? false);
  protected readonly canPublish = computed(() => {
    const status = this.exhibition()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });
  protected readonly canArchive = computed(() => {
    const status = this.exhibition()?.status;
    return this.isAdmin() && status !== undefined && status !== 'Archived';
  });

  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.exhibition() ? 'Saving…' : 'Creating…';
    return this.exhibition() ? 'Save' : 'Create exhibition';
  });

  /**
   * Once published, the address is someone's bookmark: changing it is allowed, and the old address is redirected to the
   * new one when it is saved (ADR 0035). The hint says so, and names the address that will redirect.
   */
  protected readonly slugHint = computed(() => {
    const saved = this.exhibition();
    const slug = this.draft().slug;
    if (!this.slugLocked() || !saved)
      return `6311arts.com/exhibitions/${this.draft().slug || '…'} — lower-case letters, digits and hyphens.`;
    return slug === saved.slug
      ? `6311arts.com/exhibitions/${slug} — published, so changing it sends visitors from this address to the new one.`
      : `6311arts.com/exhibitions/${slug || '…'} — on saving, visitors to /exhibitions/${saved.slug} are sent here.`;
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const exhibition = this.exhibition();
    if (!exhibition) {
      return (
        draft.title.trim() !== '' ||
        draft.summary.trim() !== '' ||
        draft.body.trim() !== '' ||
        draft.startDate !== '' ||
        draft.endDate !== '' ||
        draft.reception !== null ||
        draft.heroMediaId !== null ||
        draft.artists.length > 0
      );
    }
    return Object.keys(changesOf(exhibition, draft)).length > 0;
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());

    effect(() => {
      this.id();
      untracked(() => void this.load());
    });

    // The hero's thumbnail and alt text follow whichever image the draft names.
    effect(() => {
      const id = this.draft().heroMediaId;
      untracked(() => void this.showHero(id));
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
      this.exhibition.set(null);
      this.draft.set(EMPTY);
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      const exhibition = await firstValueFrom(this.exhibitions.get(id));
      this.exhibition.set(exhibition);
      this.draft.set(draftOf(exhibition));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce('Exhibition created. Publish it when it’s ready.');
        this.focus(this.heading);
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === EXHIBITION_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  protected patch(change: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    this.clearErrors(Object.keys(change) as Field[]);
  }

  protected patchReception(change: Partial<ReceptionDraft>, field: Field): void {
    this.draft.update((draft) =>
      draft.reception ? { ...draft, reception: { ...draft.reception, ...change } } : draft,
    );
    this.clearErrors([field]);
  }

  protected toggleReception(on: boolean): void {
    const draft = this.draft();
    if (on === (draft.reception !== null)) return;
    // A new reception starts on the show's first day; the times are left for the person to set.
    this.patch({
      reception: on ? { date: draft.startDate, starts: '', ends: '', note: '' } : null,
    });
    this.clearErrors(['receptionDate', 'receptionStarts', 'receptionEnds', 'receptionNote']);
  }

  protected setTitle(title: string): void {
    const draft = this.draft();
    // A new exhibition's address follows its title until someone types one.
    const followsTitle = !this.exhibition() && !draft.slugEdited;
    this.patch({ title, ...(followsTitle ? { slug: slugFromName(title) } : {}) });
  }

  protected setSlug(slug: string): void {
    this.patch({ slug, slugEdited: true });
  }

  protected setArtists(artists: readonly CreditedArtist[]): void {
    this.draft.update((draft) => ({ ...draft, artists }));
    this.clearErrors(['artistIds']);
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
    const exhibition = this.exhibition();
    if (exhibition) this.draft.set(draftOf(exhibition));
    this.errors.set({});
    this.formError.set(null);
  }

  protected heroWord(mine: string | null, theirs: string | null): string {
    if (!mine) return 'None';
    return mine === theirs ? 'The same image' : 'A different image';
  }

  protected namesOf(artists: readonly CreditedArtist[]): string {
    return artists.length === 0 ? 'None' : artists.map((artist) => artist.name).join(', ');
  }

  protected runOf(start: string, end: string): string {
    return start && end ? formatDateRange(start, end) : '—';
  }

  protected receptionWords(reception: ReceptionDraft | null): string {
    if (!reception) return 'None';
    const starts = instantOf(reception.date, reception.starts);
    if (!starts) return 'Not finished';
    const time = RECEPTION_TIME.format(new Date(starts));
    return reception.note.trim() ? `${time} — ${reception.note.trim()}` : time;
  }

  protected excerpt(text: string): string {
    const flat = text.replace(/\s+/g, ' ').trim();
    if (flat === '') return '—';
    return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
  }

  protected statusText(exhibition: Exhibition): string {
    switch (exhibition.status) {
      case 'Published':
        return 'Published: on the public site. Edits show there within a minute.';
      case 'Archived':
        return 'Archived: retired from the site. Archiving can’t be undone here.';
      case 'Scheduled':
        return 'Scheduled to publish.';
      default:
        return exhibition.publishedAt
          ? 'A draft again, after being published. Its web address stays fixed.'
          : 'A draft: only staff can see it. Publishing needs a summary, and alt text on the hero image.';
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

    const exhibition = this.exhibition();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!exhibition) {
        const created = await firstValueFrom(this.exhibitions.create(createOf(draft)));
        this.exhibition.set(created);
        this.draft.set(draftOf(created));
        this.status.set({ kind: 'ready' });
        // The editor for the new id is a new component; it announces the creation and takes focus.
        await this.router.navigate(['/exhibitions', created.id], {
          replaceUrl: true,
          state: { [CREATED_STATE]: true },
        });
        return;
      }

      const saved = await firstValueFrom(
        this.exhibitions.update(
          exhibition.id,
          changesOf(exhibition, draft),
          etag ?? exhibition.etag,
        ),
      );
      this.exhibition.set(saved);
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

  /** Saves the editor's own values over the version someone else saved. */
  protected async keepMine(theirs: Exhibition): Promise<void> {
    this.exhibition.set(theirs);
    this.status.set({ kind: 'ready' });
    // The comparison and its buttons are gone now, so focus moves to the title rather than being lost.
    this.focus(this.heading);
    if (!this.dirty()) {
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }
    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: Exhibition): void {
    this.exhibition.set(theirs);
    this.draft.set(draftOf(theirs));
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focus(this.heading);
  }

  protected async unpublish(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Unpublish this exhibition?',
      message:
        'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    if (confirmed) await this.run('unpublish');
  }

  protected async archive(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      heading: 'Archive this exhibition?',
      message:
        'An archived exhibition is retired from the site, and archiving can’t be undone from the CMS. The archive is recorded under your name.',
      confirmLabel: 'Archive exhibition',
      cancelLabel: 'Keep it',
    });
    if (confirmed) await this.run('archive');
  }

  /** The `schedule` command: the only one with a body, so it does not go through {@link run} (ADR 0036). */
  protected async schedule(scheduledFor: string): Promise<void> {
    const exhibition = this.exhibition();
    if (!exhibition || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command: 'schedule' });
    this.announce(COMMAND_LABELS.schedule.busy);
    try {
      const updated = await firstValueFrom(
        this.exhibitions.schedule(exhibition.id, scheduledFor, exhibition.etag),
      );
      this.exhibition.set(updated);
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

  protected async run(command: ExhibitionCommand): Promise<void> {
    const exhibition = this.exhibition();
    if (!exhibition || this.busy() || this.dirty()) return;

    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'commanding', command });
    this.announce(COMMAND_LABELS[command].busy);
    try {
      const updated = await firstValueFrom(
        this.exhibitions.command(exhibition.id, command, exhibition.etag),
      );
      this.exhibition.set(updated);
      this.draft.set(draftOf(updated));
      this.status.set({ kind: 'ready' });
      this.announce(COMMAND_LABELS[command].done);
      // The button pressed may be gone (Publish, once published), so focus goes to the panel's heading.
      this.focus(this.publishingHeading);
    } catch (error: unknown) {
      await this.commandFailed(error);
    }
  }

  private clearErrors(fields: readonly string[]): void {
    const cleared = fields.filter((key) => key in this.errors());
    if (cleared.length === 0) return;
    this.errors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([key]) => !cleared.includes(key))),
    );
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);

    if (problem?.code === EXHIBITION_CODES.concurrencyConflict && this.exhibition()) {
      try {
        const theirs = await firstValueFrom(this.exhibitions.get(this.exhibition()!.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          'Someone else saved this exhibition while you were editing. Compare the two versions.',
        );
        // Focus follows the news, so a keyboard or screen-reader user lands on the comparison.
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

  /**
   * A refused command. The form holds no unsaved edits (commands wait for a save), so the latest saved version is
   * simply shown again with the reason — a conflict, a status someone else changed, or what publishing still needs.
   */
  private async commandFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === EXHIBITION_CODES.concurrencyConflict ||
      problem?.code === EXHIBITION_CODES.transitionInvalid;

    if (stale && this.exhibition()) {
      try {
        const latest = await firstValueFrom(this.exhibitions.get(this.exhibition()!.id));
        this.exhibition.set(latest);
        this.draft.set(draftOf(latest));
      } catch {
        // The message below still says what happened.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      fieldErrorsOf(problem),
      problem?.code === EXHIBITION_CODES.concurrencyConflict
        ? 'Someone else changed this exhibition a moment ago. Here is the latest version; check it and try again.'
        : messageOf(problem, 'That didn’t work. Try again.'),
    );
  }

  private showErrors(errors: FieldErrors, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The message is linked to the field and read when it has focus (UiField), so focus goes to the first one. The
    // hero and the credits have no field of their own, so their errors send focus to what fixes them.
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const invalid = root.querySelector<HTMLElement>('[aria-invalid="true"]');
        const target =
          invalid ??
          (errors.heroMediaId ? this.heroButton()?.nativeElement : null) ??
          (errors.artistIds ? root.querySelector<HTMLElement>('[name="artistSearch"]') : null);
        target?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Writes to the live region. The same words twice still change it, so they are read out again. */
  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
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

/** A saved reception on the gallery's clock. Its day is the day it starts. */
function receptionDraftOf(reception: ExhibitionReception | null): ReceptionDraft | null {
  if (!reception) return null;
  const starts = wallClockOf(reception.startsAt);
  const ends = wallClockOf(reception.endsAt);
  if (!starts || !ends) return null;
  return { date: starts.date, starts: starts.time, ends: ends.time, note: reception.note ?? '' };
}

function draftOf(exhibition: Exhibition): Draft {
  return {
    title: exhibition.title,
    slug: exhibition.slug,
    slugEdited: true,
    summary: exhibition.summary ?? '',
    body: exhibition.body ?? '',
    startDate: exhibition.startDate,
    endDate: exhibition.endDate,
    reception: receptionDraftOf(exhibition.reception),
    heroMediaId: exhibition.heroMediaId,
    artists: exhibition.artists,
  };
}

function heroOf(asset: MediaAsset): Hero {
  const url =
    asset.renditions.find((rendition) => rendition.name === '800.webp')?.url ??
    asset.renditions.find((rendition) => rendition.name === '400.webp')?.url ??
    [...asset.renditions].sort((a, b) => a.width - b.width)[0]?.url ??
    null;
  return { id: asset.id, url, altText: asset.altText };
}

/** Long-text line endings as the API stores them, so untouched text never looks edited. */
const normalizeText = (text: string) => text.replace(/\r\n?/g, '\n').trim();

/** The reception as the API takes it: both times as instants, the note only when there is one. */
function receptionInputOf(reception: ReceptionDraft): ExhibitionReceptionInput {
  const note = reception.note.trim();
  return {
    startsAt: instantOf(reception.date, reception.starts)!,
    endsAt: instantOf(reception.date, reception.ends)!,
    ...(note ? { note } : {}),
  };
}

function sameReception(a: ReceptionDraft | null, b: ReceptionDraft | null): boolean {
  if (a === null || b === null) return a === b;
  return (
    a.date === b.date &&
    a.starts === b.starts &&
    a.ends === b.ends &&
    a.note.trim() === b.note.trim()
  );
}

function createOf(draft: Draft): ExhibitionCreate {
  const summary = normalizeText(draft.summary);
  const body = normalizeText(draft.body);
  return {
    title: draft.title.trim(),
    startDate: draft.startDate,
    endDate: draft.endDate,
    ...(draft.slug ? { slug: draft.slug } : {}),
    ...(summary ? { summary } : {}),
    ...(body ? { body } : {}),
    ...(draft.reception ? { reception: receptionInputOf(draft.reception) } : {}),
    ...(draft.heroMediaId ? { heroMediaId: draft.heroMediaId } : {}),
    ...(draft.artists.length > 0 ? { artistIds: draft.artists.map((artist) => artist.id) } : {}),
  };
}

/** Only what changed; an emptied summary or body is sent as "" so the API clears it. */
function changesOf(exhibition: Exhibition, draft: Draft): ExhibitionUpdate {
  const title = draft.title.trim();
  const summary = normalizeText(draft.summary);
  const body = normalizeText(draft.body);
  const ids = draft.artists.map((artist) => artist.id);
  const savedIds = exhibition.artists.map((artist) => artist.id);
  const reception = sameReception(draft.reception, receptionDraftOf(exhibition.reception))
    ? {}
    : draft.reception
      ? { reception: receptionInputOf(draft.reception) }
      : { removeReception: true as const };

  return {
    ...(title !== exhibition.title ? { title } : {}),
    ...(draft.slug !== exhibition.slug ? { slug: draft.slug } : {}),
    ...(summary !== (exhibition.summary ?? '') ? { summary } : {}),
    ...(body !== (exhibition.body ?? '') ? { body } : {}),
    ...(draft.startDate !== exhibition.startDate ? { startDate: draft.startDate } : {}),
    ...(draft.endDate !== exhibition.endDate ? { endDate: draft.endDate } : {}),
    ...reception,
    ...(draft.heroMediaId !== exhibition.heroMediaId
      ? draft.heroMediaId
        ? { heroMediaId: draft.heroMediaId }
        : { removeHero: true as const }
      : {}),
    ...(ids.length !== savedIds.length || ids.some((id, index) => id !== savedIds[index])
      ? { artistIds: ids }
      : {}),
  };
}

/** What the form can tell before asking: the API checks the same and has the last word. */
function localErrors(draft: Draft): FieldErrors {
  const errors: FieldErrors = {};
  if (draft.title.trim() === '') errors.title = 'Enter the exhibition’s title.';
  if (!isSlug(draft.slug)) {
    errors.slug = draft.slug
      ? 'Use lower-case letters, digits and single hyphens (for example boats-and-pianos).'
      : 'Enter a web address, for example boats-and-pianos.';
  }
  if (!draft.startDate) errors.startDate = 'Enter the first day of the show.';
  if (!draft.endDate) errors.endDate = 'Enter the last day of the show.';
  else if (draft.startDate && draft.endDate < draft.startDate) {
    errors.endDate = 'The show can’t end before it starts.';
  }

  const reception = draft.reception;
  if (reception) {
    if (!reception.date) errors.receptionDate = 'Enter the day of the reception.';
    if (!reception.starts) errors.receptionStarts = 'Enter when the reception starts.';
    if (!reception.ends) errors.receptionEnds = 'Enter when the reception ends.';
    else if (reception.starts && reception.ends <= reception.starts) {
      errors.receptionEnds = 'The reception must end after it starts, on the same day.';
    }
  }
  return errors;
}

function problemOf(error: unknown): ExhibitionProblem | null {
  return error instanceof HttpErrorResponse
    ? decodeExhibitionProblem(error.error, error.status)
    : null;
}

function fieldErrorsOf(problem: ExhibitionProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const [key, messages] of Object.entries(problem?.fieldErrors ?? {})) {
    const field = API_FIELDS[key] ?? (FIELDS as readonly string[]).find((name) => name === key);
    if (field && messages[0] && !errors[field as Field]) errors[field as Field] = messages[0];
  }
  return errors;
}

function messageOf(problem: ExhibitionProblem | null, fallback: string): string {
  if (problem === null || problem.status === 0) {
    return 'Can’t reach 6311arts. Your changes are still here; try again.';
  }
  const message = problem.detail ?? fallback;

  // A 5xx says nothing the editor can act on, so hand them the one thing support can: the correlation id,
  // which is this request's trace id (ADR 0019). Appended here rather than left to the global problem toast,
  // because these calls deliberately suppress it to show the failure in place.
  return problem.status >= 500 && problem.correlationId !== null
    ? `${message} Quote reference ${problem.correlationId} if you report it.`
    : message;
}
