import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import { formatDateRange, instantOf, wallClockOf } from '../../../core/time/gallery-time';
import {
  CreditedArtist,
  EXHIBITION_LIMITS,
  Exhibition,
  ExhibitionCreate,
  ExhibitionReception,
  ExhibitionReceptionInput,
  ExhibitionUpdate,
} from '../../../models/exhibition';
import { ExhibitionService } from '../../../services/exhibition.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { ImagePanel } from '../../../shared/image-panel/image-panel';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import {
  CompareRow,
  FieldErrors,
  Focusable,
  excerpt,
  imageRow,
  orDash,
} from '../../../shared/record-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';
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
  readonly summary: string;
  readonly body: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reception: ReceptionDraft | null;
  readonly heroMediaId: string | null;
  readonly artists: readonly CreditedArtist[];
}

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

const EMPTY: Draft = {
  title: '',
  slug: '',
  summary: '',
  body: '',
  startDate: '',
  endDate: '',
  reception: null,
  heroMediaId: null,
  artists: [],
};

const RECEPTION_TIME = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'America/Chicago',
});

/**
 * One exhibition (`/admin/exhibitions/new`, `/admin/exhibitions/:id`): title, web address, summary, body, when it
 * hangs, its opening reception, its hero image and the artists it credits; then publish, unpublish or (Admins)
 * archive. Reception times are typed and shown on the gallery's clock and travel as UTC.
 */
@Component({
  selector: 'cms-exhibition-editor',
  imports: [
    FormsModule,
    UiControl,
    UiField,
    ArtistCredits,
    EditorFrame,
    ImagePanel,
    PublishingPanel,
    SaveActions,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exhibition-editor.html',
  styleUrl: './exhibition-editor.scss',
})
export class ExhibitionEditor extends ContentEditor<
  Exhibition,
  Draft,
  ExhibitionCreate,
  ExhibitionUpdate,
  Field
> {
  private readonly hero = viewChild(ImagePanel);

  protected readonly limits = EXHIBITION_LIMITS;
  protected readonly exhibition = this.record;

  constructor() {
    super({
      api: inject(ExhibitionService),
      noun: 'exhibition',
      plural: 'exhibitions',
      route: '/exhibitions',
      label: (exhibition) => exhibition.title,
      address: { path: '6311arts.com/exhibitions/', from: 'title' },
      createdMessage: 'Exhibition created. Publish it when it’s ready.',
      createdState: 'exhibitionCreated',
      empty: () => EMPTY,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
        draft.title.trim() !== '' ||
        draft.summary.trim() !== '' ||
        draft.body.trim() !== '' ||
        draft.startDate !== '' ||
        draft.endDate !== '' ||
        draft.reception !== null ||
        draft.heroMediaId !== null ||
        draft.artists.length > 0,
      localErrors,
      fields: FIELDS,
      // The API's field names for the reception, as the form's own.
      apiFields: {
        'reception.startsAt': 'receptionStarts',
        'reception.endsAt': 'receptionEnds',
        'reception.note': 'receptionNote',
      },
      lifecycle: {
        noun: 'exhibition',
        keepLabel: 'Keep it',
        inSquare: false,
        previewType: 'exhibition',
        unpublishMessage:
          'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
        archiveMessage:
          'An archived exhibition is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The exhibition leaves the site and the list, and its web address is free again. An Admin can restore it from Deleted, as a draft.',
        draft:
          'A draft: only staff can see it. Publishing needs a summary, and alt text on the hero image.',
      },
    });
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

  protected override compare(theirs: Exhibition, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Title', mine: orDash(draft.title), theirs: theirs.title },
      {
        label: 'Web address',
        mine: `/exhibitions/${draft.slug}`,
        theirs: `/exhibitions/${theirs.slug}`,
      },
      {
        label: 'Dates',
        mine: runOf(draft.startDate, draft.endDate),
        theirs: runOf(theirs.startDate, theirs.endDate),
      },
      {
        label: 'Reception',
        mine: receptionWords(draft.reception),
        theirs: receptionWords(receptionDraftOf(theirs.reception)),
      },
      { label: 'Summary', mine: excerpt(draft.summary), theirs: excerpt(theirs.summary) },
      { label: 'About the show', mine: excerpt(draft.body), theirs: excerpt(theirs.body) },
      imageRow('Hero image', draft.heroMediaId, theirs.heroMediaId),
      { label: 'Artists', mine: namesOf(draft.artists), theirs: namesOf(theirs.artists) },
    ];
  }

  protected override fieldsOf(keys: readonly string[]): readonly string[] {
    return keys.map((key) => (key === 'artists' ? 'artistIds' : key));
  }

  /** The hero and the credits have no field of their own, so their errors send focus to what fixes them. */
  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    if (errors.heroMediaId) return this.hero()?.focusTarget();
    return errors.artistIds
      ? this.host.nativeElement.querySelector<HTMLElement>('[name="artistSearch"]')
      : null;
  }
}

function namesOf(artists: readonly CreditedArtist[]): string {
  return artists.length === 0 ? 'None' : artists.map((artist) => artist.name).join(', ');
}

function runOf(start: string, end: string): string {
  return start && end ? formatDateRange(start, end) : '—';
}

function receptionWords(reception: ReceptionDraft | null): string {
  if (!reception) return 'None';
  const starts = instantOf(reception.date, reception.starts);
  if (!starts) return 'Not finished';
  const time = RECEPTION_TIME.format(new Date(starts));
  return reception.note.trim() ? `${time} — ${reception.note.trim()}` : time;
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
    summary: exhibition.summary ?? '',
    body: exhibition.body ?? '',
    startDate: exhibition.startDate,
    endDate: exhibition.endDate,
    reception: receptionDraftOf(exhibition.reception),
    heroMediaId: exhibition.heroMediaId,
    artists: exhibition.artists,
  };
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

function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
  if (draft.title.trim() === '') errors.title = 'Enter the exhibition’s title.';
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
