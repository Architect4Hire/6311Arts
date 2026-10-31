import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiControl, UiField } from '@6311/ui';
import { CLASS_LIMITS, ClassCreate, ClassOffering, ClassUpdate } from '../../../models/class';
import { ClassService } from '../../../services/class.service';
import { ContentEditor } from '../../../shared/content-editor';
import { EditorFrame } from '../../../shared/editor-frame/editor-frame';
import { ImagePanel } from '../../../shared/image-panel/image-panel';
import { PublishingPanel } from '../../../shared/publishing-panel/publishing-panel';
import { CompareRow, FieldErrors, Focusable, orDash } from '../../../shared/record-editor';
import { RichTextEditor } from '../../../shared/rich-text-editor/rich-text-editor';
import { SaveActions } from '../../../shared/save-actions/save-actions';
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
  readonly summary: string;
  /** HTML, as the rich-text editor holds it. */
  readonly body: string;
  readonly classType: string;
  readonly level: string;
  readonly heroMediaId: string | null;
  /** The terms it is offered in, each with its instructor, sessions, seats and price (ADR 0044). */
  readonly runs: readonly RunDraft[];
}

const FIELDS = [
  'title',
  'slug',
  'summary',
  'body',
  'classType',
  'level',
  'heroMediaId',
  'runs',
] as const;
type Field = (typeof FIELDS)[number];

/** A new class starts with one empty run, which is not an edit until something is typed into it. */
const empty = (): Draft => ({
  title: '',
  slug: '',
  summary: '',
  body: '',
  classType: '',
  level: '',
  heroMediaId: null,
  runs: [newRunDraft([], null)],
});

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
 */
@Component({
  selector: 'cms-class-editor',
  imports: [
    FormsModule,
    UiControl,
    UiField,
    RichTextEditor,
    ClassRuns,
    EditorFrame,
    ImagePanel,
    PublishingPanel,
    SaveActions,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './class-editor.html',
  styleUrl: './class-editor.scss',
})
export class ClassEditor extends ContentEditor<
  ClassOffering,
  Draft,
  ClassCreate,
  ClassUpdate,
  Field
> {
  private readonly hero = viewChild(ImagePanel);
  private readonly bodyEditor = viewChild(RichTextEditor);

  protected readonly limits = CLASS_LIMITS;
  protected readonly offering = this.record;

  /** The currency the API returns amounts in: every run's is the same. */
  protected readonly currency = computed(() => this.offering()?.runs[0]?.currency ?? 'USD');

  constructor() {
    super({
      api: inject(ClassService),
      noun: 'class',
      plural: 'classes',
      route: '/classes',
      label: (offering) => offering.title,
      address: { path: '6311arts.com/classes/', from: 'title' },
      createdMessage: 'Class created. Publish it when it’s ready.',
      createdState: 'classCreated',
      empty,
      draftOf,
      createOf,
      changesOf,
      started: (draft) =>
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
        ),
      localErrors,
      fields: FIELDS,
      lifecycle: {
        noun: 'class',
        keepLabel: 'Keep it',
        inSquare: true,
        previewType: 'class-offering',
        unpublishMessage:
          'It goes back to being a draft and leaves the public site. Its web address stays the same, so publishing again restores it.',
        archiveMessage:
          'An archived class is retired from the site, and archiving can’t be undone from Studio. The archive is recorded under your name.',
        deleteText:
          'The class leaves the site and the list, and its web address is free again. An Admin can restore it from Deleted, as a draft.',
        redrafted: 'A draft again, after being published. Its web address stays as it is.',
        draft:
          'A draft: only staff can see it. Publishing needs a summary and at least one run, each with an instructor, a price and a session.',
      },
    });
  }

  /**
   * Asked structurally rather than from what would be sent: a run that is still half filled in is a change the
   * person can lose, and it is `save` that refuses it with a message.
   */
  protected override hasChanges(offering: ClassOffering, draft: Draft): boolean {
    return runsChanged(offering, draft) || super.hasChanges(offering, draft);
  }

  protected override compare(theirs: ClassOffering, draft: Draft): readonly CompareRow[] {
    return [
      { label: 'Title', mine: orDash(draft.title), theirs: theirs.title },
      { label: 'Web address', mine: `/classes/${draft.slug}`, theirs: `/classes/${theirs.slug}` },
      { label: 'Summary', mine: orDash(draft.summary), theirs: orDash(theirs.summary) },
      {
        label: 'Runs',
        mine: describeRuns(draft.runs),
        theirs: describeRuns(runDraftsOf(theirs.runs)),
      },
      {
        label: 'More details',
        mine: `${draft.body.length} characters`,
        theirs: `${(theirs.body ?? '').length} characters`,
      },
      { label: 'Class type', mine: orDash(draft.classType), theirs: orDash(theirs.classType) },
      { label: 'Level', mine: orDash(draft.level), theirs: orDash(theirs.level) },
      {
        label: 'Lead photograph',
        mine: draft.heroMediaId ? 'A photograph' : 'None',
        theirs: theirs.heroMediaId ? 'A photograph' : 'None',
      },
    ];
  }

  /** The description's error is the rich text's own; the hero's goes to the button that changes it. */
  protected override focusFallback(errors: FieldErrors<Field>): Focusable | null | undefined {
    if (errors.body) return this.bodyEditor();
    return errors.heroMediaId ? this.hero()?.focusTarget() : null;
  }
}

/** The runs in a line each comparison cell can hold: who teaches each, from when, and for how many sittings. */
function describeRuns(runs: readonly RunDraft[]): string {
  if (runs.length === 0) return 'None';
  return runs
    .map((run, index) => {
      const first = run.sessions[0];
      const when = first?.date ? ` from ${first.date}` : '';
      return `${runName(run, index)}: ${run.instructor?.name ?? 'no instructor'}${when}, ${run.sessions.length} session${run.sessions.length === 1 ? '' : 's'}`;
    })
    .join('; ');
}

function draftOf(offering: ClassOffering): Draft {
  return {
    title: offering.title,
    slug: offering.slug,
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

function localErrors(draft: Draft): FieldErrors<Field> {
  const errors: FieldErrors<Field> = {};
  if (draft.title.trim() === '') errors.title = 'Enter the class’s title.';
  const runError = runErrorOf(draft.runs);
  if (runError) errors.runs = runError;
  return errors;
}
