import {
  Directive,
  ElementRef,
  Injector,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { HasUnsavedChanges, warnBeforeUnload } from '../core/guards/unsaved-changes.guard';
import { messageOf, problemOf } from '../core/http/problem-message';
import { IconName, StatusTone } from '@6311/ui';
import { CONTENT_CODES, slugFromName } from '../models/content';
import { ApiProblem } from '../models/decode';

/** What every editable record carries: its id and the row version a write must name. */
export interface EditableRecord {
  readonly id: string;
  readonly etag: string;
}

/** Where an editor is: loading, ready, saving, or comparing its draft with a version someone else saved. */
export type EditorStatus<T> =
  | { readonly kind: 'loading' }
  | { readonly kind: 'load-failed' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'saving' }
  | { readonly kind: 'conflict'; readonly theirs: T };

/** One message per field the form shows, keyed by the form's own field names. */
export type FieldErrors<F extends string> = Partial<Record<F, string>>;

/** One row of the 409 comparison: a field, as this editor has it and as it was saved. */
export interface CompareRow {
  readonly label: string;
  readonly mine: string;
  readonly theirs: string;
}

/** How the record's state reads beside its title: a word, a glyph and a tone, never colour alone. */
export interface EditorChip {
  readonly label: string;
  readonly icon: IconName;
  readonly tone: StatusTone;
}

/** What `<cms-editor-frame>` and `<cms-save-actions>` show of an editor, and what they can ask of it. */
export interface EditorView {
  readonly noun: string;
  readonly plural: string;
  readonly listName: string;
  readonly route: string;
  readonly status: Signal<EditorStatus<unknown>>;
  readonly title: Signal<string>;
  readonly chip: Signal<EditorChip | null>;
  readonly liveMessage: Signal<string>;
  readonly formError: Signal<string | null>;
  /** The 409 comparison, or `null` when there is no conflict. */
  readonly comparison: Signal<readonly CompareRow[] | null>;
  readonly saving: Signal<boolean>;
  readonly busy: Signal<boolean>;
  readonly dirty: Signal<boolean>;
  readonly saveLabel: Signal<string>;
  /** A saved record with edits: they can be thrown away. */
  readonly canDiscard: Signal<boolean>;
  retry(): void;
  keepMine(): void;
  useTheirs(): void;
  discard(): void;
}

/** The reads and writes an editor makes, as each type's service already offers them. */
export interface RecordEditorApi<T, C, U> {
  get(id: string): Observable<T>;
  create(create: C): Observable<T>;
  update(id: string, update: U, etag: string): Observable<T>;
}

/** What one editor is about; everything else an editor does is the same for every type. */
export interface RecordEditorConfig<
  T extends EditableRecord,
  D,
  C,
  U extends object,
  F extends string,
> {
  readonly api: RecordEditorApi<T, C, U>;
  /** How messages name the record: "artist", "piece". */
  readonly noun: string;
  /** "artists", as in "All artists". */
  readonly plural: string;
  /** "Back to the …", where the list is not simply the plural ("shop"). */
  readonly listName?: string;
  /** The list it belongs to, which is also the stem of its editor's address: "/artists". */
  readonly route: string;
  /** What staff call a saved record: its name or title. */
  readonly label: (saved: T) => string;
  /**
   * Where the record lives on the site, before its slug ("6311arts.com/artists/"), and what the slug is made from
   * on create; for {@link RecordEditor.slugHint}. A type with no address of its own leaves it out.
   */
  readonly address?: {
    readonly path: string;
    readonly from: 'name' | 'title';
    /** Who else may hold the address, where that is more than another of its type: "another page or the site". */
    readonly clash?: string;
  };
  /** Read out when the new record's editor opens. */
  readonly createdMessage: string;
  /** The router state key "Create" sets for the new record's editor (a new component) to find: "artistCreated". */
  readonly createdState: string;
  /** The draft a new record starts from. */
  readonly empty: () => D;
  readonly draftOf: (saved: T) => D;
  readonly createOf: (draft: D) => C;
  /** Only what changed; an empty object is "nothing to save". */
  readonly changesOf: (saved: T, draft: D) => U;
  /** Whether a new record's draft holds anything worth asking about before it is lost. */
  readonly started: (draft: D) => boolean;
  /** What the form can tell before asking: the API checks the same and has the last word. */
  readonly localErrors: (draft: D) => FieldErrors<F>;
  /** The form's fields, as the API names them in its field errors. */
  readonly fields: readonly F[];
  /** API field names that are not the form's own (`reception.startsAt` → `receptionStarts`). */
  readonly apiFields?: Readonly<Record<string, F>>;
}

/** Something an error can send focus to: an element, or a component such as the rich-text editor. */
export interface Focusable {
  focus(): void;
}

/** A comparison cell for an optional text: the text, or a dash. */
export function orDash(text: string | null | undefined): string {
  return text || '—';
}

export function yesNo(value: boolean): string {
  return value ? 'Yes' : 'No';
}

/** A long text as one line of at most 80 characters, for the comparison table. */
export function excerpt(text: string | null | undefined): string {
  const flat = (text ?? '').replace(/\s+/g, ' ').trim();
  if (flat === '') return '—';
  return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
}

/** The words of an HTML body, for the comparison. Parsed inert: a template's content runs and loads nothing. */
export function htmlExcerpt(html: string | null | undefined): string {
  const template = document.createElement('template');
  template.innerHTML = html ?? '';
  const flat = (template.content.textContent ?? '').replace(/s+/g, ' ').trim();
  if (flat === '') return '—';
  return flat.length > 120 ? `${flat.slice(0, 120)}…` : flat;
}

/** The comparison row for an image: whether this editor kept the saved one, chose another, or has none. */
export function imageRow(label: string, mine: string | null, theirs: string | null): CompareRow {
  return {
    label,
    mine: !mine ? 'None' : mine === theirs ? 'The same image' : 'A different image',
    theirs: theirs ? 'An image' : 'None',
  };
}

/**
 * The editor every CMS record shares (`/admin/{type}/new`, `/admin/{type}/:id`): load, a draft of the form, save, and
 * a 409 shown as both versions. A type's editor supplies its form and its {@link RecordEditorConfig}.
 *
 * Unsaved edits survive every recoverable failure (frontend.md): a refused save keeps the form as typed, and a 409
 * shows the saved version beside the editor's own so neither is lost.
 */
@Directive()
export abstract class RecordEditor<
  T extends EditableRecord,
  D,
  C,
  U extends object,
  F extends string,
> implements HasUnsavedChanges {
  /** The route's `:id`; absent on `/{type}/new`. */
  readonly id = input<string>();

  protected readonly router = inject(Router);
  protected readonly injector = inject(Injector);
  protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Arrived straight from "Create", which navigated here from `/{type}/new` (another component). */
  private readonly justCreated: boolean;

  protected readonly status = signal<EditorStatus<T>>({ kind: 'loading' });
  /** The record as saved; `null` while creating one. */
  protected readonly record = signal<T | null>(null);
  protected readonly draft: WritableSignal<D>;
  protected readonly errors = signal<FieldErrors<F>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly liveMessage = signal('');

  /** A save in flight, or whatever else the type's editor counts as one ({@link pending}). */
  protected readonly busy: Signal<boolean> = computed(
    () => this.status().kind === 'saving' || this.pending(),
  );
  protected readonly conflict = computed(() => {
    const status = this.status();
    return status.kind === 'conflict' ? status.theirs : null;
  });
  protected readonly saveLabel = computed(() => {
    if (this.status().kind === 'saving') return this.record() ? 'Saving…' : 'Creating…';
    return this.record() ? 'Save' : `Create ${this.config.noun}`;
  });

  /** Edits not yet saved: the unsaved-changes guard asks before they are lost. */
  readonly dirty = computed(() => {
    const draft = this.draft();
    const saved = this.record();
    return saved ? this.hasChanges(saved, draft) : this.config.started(draft);
  });

  /** Made from the name on create and never typed (the field only shows it), so the address never moves. */
  protected readonly slugHint = computed(() => {
    const address = this.config.address;
    const slug = (this.draft() as { readonly slug?: string }).slug ?? '';
    if (!address) return '';
    return this.record()
      ? `${address.path}${slug}`
      : `${address.path}${slug || '…'} — made from the ${address.from}. If ${address.clash ?? `another ${this.config.noun}`} has it, a number is added.`;
  });

  /** Past loading: the form is shown (ready, saving or comparing). */
  protected readonly loaded = computed(() => {
    const kind = this.status().kind;
    return kind !== 'loading' && kind !== 'load-failed' && kind !== 'not-found';
  });

  /** What the frame and the save buttons show; see {@link EditorView}. */
  readonly view: EditorView;

  constructor(protected readonly config: RecordEditorConfig<T, D, C, U, F>) {
    this.draft = signal<D>(config.empty());
    this.justCreated =
      this.router.currentNavigation()?.extras.state?.[config.createdState] === true;
    this.view = {
      noun: config.noun,
      plural: config.plural,
      listName: config.listName ?? config.plural,
      route: config.route,
      status: this.status,
      title: computed(() => {
        const saved = this.record();
        return (saved && config.label(saved)) || `New ${config.noun}`;
      }),
      chip: computed(() => {
        const saved = this.record();
        return saved ? this.chipOf(saved) : null;
      }),
      liveMessage: this.liveMessage,
      formError: this.formError,
      comparison: computed(() => {
        const theirs = this.conflict();
        return theirs ? this.compare(theirs, this.draft()) : null;
      }),
      saving: computed(() => this.status().kind === 'saving'),
      busy: this.busy,
      dirty: this.dirty,
      saveLabel: this.saveLabel,
      canDiscard: computed(() => this.record() !== null && this.dirty()),
      retry: () => void this.load(),
      keepMine: () => {
        const theirs = this.conflict();
        if (theirs) void this.keepMine(theirs);
      },
      useTheirs: () => {
        const theirs = this.conflict();
        if (theirs) this.useTheirs(theirs);
      },
      discard: () => this.discard(),
    };
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
      this.record.set(null);
      this.draft.set(this.config.empty());
      this.status.set({ kind: 'ready' });
      return;
    }

    this.status.set({ kind: 'loading' });
    try {
      this.show(await firstValueFrom(this.config.api.get(id)));
      this.status.set({ kind: 'ready' });
      if (this.justCreated) {
        this.announce(this.config.createdMessage);
        this.focusHeading();
      }
    } catch (error: unknown) {
      this.status.set(
        problemOf(error)?.code === CONTENT_CODES.notFound
          ? { kind: 'not-found' }
          : { kind: 'load-failed' },
      );
    }
  }

  /** Whether the draft says something the saved record does not; by default, whether there is anything to send. */
  protected hasChanges(saved: T, draft: D): boolean {
    return Object.keys(this.config.changesOf(saved, draft)).length > 0;
  }

  /** The 409 comparison's rows: each field, as this editor has it and as it was saved. */
  protected abstract compare(theirs: T, draft: D): readonly CompareRow[];

  /** How the record's state reads beside its title, if it has one. */
  protected chipOf(_saved: T): EditorChip | null {
    return null;
  }

  /** Something else the type's editor is doing that the form waits for, such as a status command. */
  protected pending(): boolean {
    return false;
  }

  /** The form fields whose errors a draft change clears; by default each changed key is its own field. */
  protected fieldsOf(keys: readonly string[]): readonly string[] {
    return keys;
  }

  /** Where focus goes for an error with no input of its own (an image, a list), or nothing. */
  protected focusFallback(_errors: FieldErrors<F>): Focusable | null | undefined {
    return null;
  }

  protected patch(change: Partial<D>): void {
    this.draft.update((draft) => ({ ...draft, ...change }));
    this.clearErrors(this.fieldsOf(Object.keys(change)));
  }

  /** A new record's address follows its name or title; staff never type one, and the API numbers a taken one. */
  protected rename(field: 'name' | 'title', value: string): void {
    const slug = this.record() ? {} : { slug: slugFromName(value) };
    this.patch({ [field]: value, ...slug } as Partial<D>);
  }

  protected clearErrors(fields: readonly string[]): void {
    const cleared = fields.filter((key) => key in this.errors());
    if (cleared.length === 0) return;
    this.errors.update(
      (errors) =>
        Object.fromEntries(
          Object.entries(errors).filter(([key]) => !cleared.includes(key)),
        ) as FieldErrors<F>,
    );
  }

  protected discard(): void {
    const saved = this.record();
    if (saved) this.draft.set(this.config.draftOf(saved));
    this.errors.set({});
    this.formError.set(null);
  }

  /** A record as saved, form included. */
  protected show(saved: T): void {
    this.record.set(saved);
    this.draft.set(this.config.draftOf(saved));
  }

  protected async save(etag?: string): Promise<void> {
    if (this.busy() || !this.dirty()) return;

    const draft = this.draft();
    const local = this.config.localErrors(draft);
    if (Object.keys(local).length > 0) {
      this.showErrors(local, null);
      return;
    }

    const saved = this.record();
    this.errors.set({});
    this.formError.set(null);
    this.status.set({ kind: 'saving' });
    try {
      if (!saved) {
        const created = await firstValueFrom(this.config.api.create(this.config.createOf(draft)));
        this.show(created);
        this.status.set({ kind: 'ready' });
        // The editor for the new id is a new component; it announces the creation and takes focus.
        await this.router.navigate([this.config.route, created.id], {
          replaceUrl: true,
          state: { [this.config.createdState]: true },
        });
        return;
      }

      this.show(
        await firstValueFrom(
          this.config.api.update(saved.id, this.config.changesOf(saved, draft), etag ?? saved.etag),
        ),
      );
      this.status.set({ kind: 'ready' });
      // Saving disabled the fieldset that held the focused control, so the browser dropped focus to <body>.
      // Put it on the heading, which is where "Saved." is announced from, rather than leaving a keyboard or
      // screen-reader user with no position on the page (WCAG 2.4.3 focus order).
      this.focusHeading();
      this.announce('Saved.');
    } catch (error: unknown) {
      await this.saveFailed(error);
    }
  }

  /** Saves the editor's own values over the version someone else saved. */
  protected async keepMine(theirs: T): Promise<void> {
    this.status.set({ kind: 'ready' });
    // The comparison and its buttons are gone now, so focus moves to the title rather than being lost.
    this.focusHeading();

    // Deliberately *not* adopting their version before saving. `save` diffs the draft against `record()`, so
    // adopting theirs first would make every field they changed — and this editor only loaded, never touched —
    // look like one of "my changes", and send a stale copy straight back over it. Diffing against what this editor
    // loaded sends only the fields it actually edited; their etag is what lets the write through. That is what
    // frontend.md's "a 409 keeps the editor's changes" has to mean, or keeping mine silently discards theirs.
    if (!this.dirty()) {
      this.show(theirs);
      this.announce('Your changes already match the saved version. Nothing more to save.');
      return;
    }

    await this.save(theirs.etag);
  }

  protected useTheirs(theirs: T): void {
    this.show(theirs);
    this.status.set({ kind: 'ready' });
    this.announce('Showing the saved version. Your changes were discarded.');
    this.focusHeading();
  }

  protected showErrors(errors: FieldErrors<F>, message: string | null): void {
    this.errors.set(errors);
    this.formError.set(message);
    this.announce(message ?? 'Some fields need attention.');
    // The message is linked to the field and read when it has focus (UiField), so focus goes to the first one.
    // An error with no input of its own sends focus to what fixes it.
    afterNextRender(
      () => {
        const invalid = this.host.nativeElement.querySelector<HTMLElement>(
          'input[aria-invalid="true"], textarea[aria-invalid="true"], select[aria-invalid="true"]',
        );
        (invalid ?? this.focusFallback(errors))?.focus();
      },
      { injector: this.injector },
    );
  }

  /** The API's field errors, as the form's own fields; the first message for a field wins. */
  protected fieldErrorsOf(problem: ApiProblem | null): FieldErrors<F> {
    const errors: FieldErrors<F> = {};
    const fields: readonly string[] = this.config.fields;
    for (const [key, messages] of Object.entries(problem?.fieldErrors ?? {})) {
      const field = this.config.apiFields?.[key] ?? (fields.includes(key) ? (key as F) : null);
      if (field && messages[0] && !errors[field]) errors[field] = messages[0];
    }
    return errors;
  }

  /** Writes to the live region. The same words twice still change it, so they are read out again. */
  protected announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  protected focusHeading(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('h1')?.focus(), {
      injector: this.injector,
    });
  }

  private async saveFailed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const saved = this.record();

    if (problem?.code === CONTENT_CODES.concurrencyConflict && saved) {
      try {
        const theirs = await firstValueFrom(this.config.api.get(saved.id));
        this.status.set({ kind: 'conflict', theirs });
        this.announce(
          `Someone else saved this ${this.config.noun} while you were editing. Compare the two versions.`,
        );
        // Focus follows the news, so a keyboard or screen-reader user lands on the comparison.
        afterNextRender(
          () => this.host.nativeElement.querySelector<HTMLElement>('.conflict')?.focus(),
          { injector: this.injector },
        );
        return;
      } catch {
        // Fall through to the plain message; the draft is still in the form.
      }
    }

    this.status.set({ kind: 'ready' });
    this.showErrors(
      this.fieldErrorsOf(problem),
      messageOf(problem, 'The changes couldn’t be saved. Try again.'),
    );
  }
}
