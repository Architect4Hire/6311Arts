import { Signal, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { ConfirmService } from '../core/confirm/confirm.service';
import { messageOf, problemOf } from '../core/http/problem-message';
import { CONTENT_CODES, ContentCommand, PreviewType, PublishStatus } from '../models/content';
import { ApiProblem } from '../models/decode';
import { confirmDelete } from './delete-confirm/delete-confirm';

/** What every publishable record carries that the lifecycle acts on (content.md "Lifecycle"). */
export interface LifecycleRecord {
  readonly id: string;
  readonly etag: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
}

/** The status commands and the delete, as each type's service already offers them. */
export interface ContentLifecycleApi<T extends LifecycleRecord> {
  get(id: string): Observable<T>;
  command(id: string, command: Exclude<ContentCommand, 'schedule'>, etag: string): Observable<T>;
  schedule(id: string, scheduledFor: string, etag: string): Observable<T>;
  delete(id: string, etag: string): Observable<void>;
}

/**
 * How one type is talked about on its publishing panel and in its dialogs. Only the words differ between types;
 * what the panel does is the same for all seven.
 */
export interface LifecycleCopy {
  /** How the panel names the record: "artist", "tier". */
  readonly noun: string;
  /** How the delete names it, where that differs from {@link noun} ("plan" for a membership tier). */
  readonly deleteNoun?: string;
  /** "Keep them" or "Keep it", for the record's own grammar. */
  readonly keepLabel: string;
  /** Whether Square holds this record too, so a delete asks a second time (ADR 0052). */
  readonly inSquare: boolean;
  readonly previewType: PreviewType;
  readonly unpublishMessage: string;
  readonly archiveMessage: string;
  /** The danger zone's paragraph: what a delete does to this record. */
  readonly deleteText: string;
  /** The panel's status line per status; the common wording is the default. */
  readonly published?: string;
  readonly archived?: string;
  readonly redrafted?: string;
  readonly draft?: string;
}

/** What the panel needs of a {@link ContentLifecycle}, whatever its record type. */
export interface LifecycleActions {
  readonly copy: LifecycleCopy;
  /** Which status command is in flight, so only its own button spins. */
  readonly commanding: Signal<ContentCommand | null>;
  readonly deleting: Signal<boolean>;
  /** Each resolves `true` when the command was applied. */
  publish(): Promise<boolean>;
  unpublish(): Promise<boolean>;
  archive(): Promise<boolean>;
  unschedule(): Promise<boolean>;
  schedule(scheduledFor: string): Promise<boolean>;
  remove(): Promise<void>;
}

/** What the editor that owns a {@link ContentLifecycle} hands it: its record, its form state and its messages. */
export interface ContentLifecycleHost<T extends LifecycleRecord> {
  readonly copy: LifecycleCopy;
  readonly api: ContentLifecycleApi<T>;
  /** The record as saved. */
  readonly record: () => T | null;
  /** What staff call it in the delete dialog: its name or title. */
  readonly label: (record: T) => string;
  readonly dirty: () => boolean;
  /** The editor's own busy state, a save included. */
  readonly busy: () => boolean;
  /** A newer saved version arrived: the editor shows it, form included. */
  readonly replace: (record: T) => void;
  readonly clearErrors: () => void;
  /** A refusal, with the problem whose field errors the form can show (none for a delete). */
  readonly refused: (message: string, problem: ApiProblem | null) => void;
  readonly announce: (message: string) => void;
  /** The record is gone: the editor leaves its screen. */
  readonly deleted: (record: T) => Promise<unknown>;
}

const DEFAULT_STATUS_TEXT = {
  published: 'Published: on the public site. Edits show there within a minute.',
  archived: 'Archived: retired from the site. Archiving can’t be undone here.',
  redrafted: 'A draft again, after being published. Its web address stays fixed.',
  draft: 'A draft: only staff can see it.',
} as const;

/** The panel's one-line account of where the record stands. */
export function statusTextOf(record: LifecycleRecord, copy: LifecycleCopy): string {
  switch (record.status) {
    case 'Published':
      return copy.published ?? DEFAULT_STATUS_TEXT.published;
    case 'Archived':
      return copy.archived ?? DEFAULT_STATUS_TEXT.archived;
    case 'Scheduled':
      return 'Scheduled to publish.';
    default:
      return record.publishedAt
        ? (copy.redrafted ?? DEFAULT_STATUS_TEXT.redrafted)
        : (copy.draft ?? DEFAULT_STATUS_TEXT.draft);
  }
}

/**
 * The publishing lifecycle of one content editor (content.md, ADR 0036, ADR 0052): publish, unpublish, schedule,
 * unschedule, archive and delete, each under the row version the editor read, with the same confirmations and
 * the same handling of a refusal for every type. `<cms-publishing-panel>` is its face.
 *
 * Status commands act on the saved version, so they wait until the form is saved; a delete does not, and its
 * dialog says the unsaved edits go with it. Create it in a field initializer: it injects the confirm service.
 */
export class ContentLifecycle<T extends LifecycleRecord> implements LifecycleActions {
  private readonly confirm = inject(ConfirmService);
  private readonly pending = signal<ContentCommand | 'delete' | null>(null);

  readonly copy: LifecycleCopy;
  readonly commanding = computed(() => {
    const pending = this.pending();
    return pending === 'delete' ? null : pending;
  });
  readonly deleting = computed(() => this.pending() === 'delete');
  /** A command or a delete is in flight; the editor counts it as busy. */
  readonly busy = computed(() => this.pending() !== null);

  constructor(private readonly host: ContentLifecycleHost<T>) {
    this.copy = host.copy;
  }

  publish(): Promise<boolean> {
    return this.run('publish');
  }

  async unpublish(): Promise<boolean> {
    const confirmed = await this.confirm.confirm({
      heading: `Unpublish this ${this.copy.noun}?`,
      message: this.copy.unpublishMessage,
      confirmLabel: 'Unpublish',
      cancelLabel: 'Keep published',
    });
    return confirmed && this.run('unpublish');
  }

  async archive(): Promise<boolean> {
    const confirmed = await this.confirm.confirm({
      heading: `Archive this ${this.copy.noun}?`,
      message: this.copy.archiveMessage,
      confirmLabel: `Archive ${this.copy.noun}`,
      cancelLabel: this.copy.keepLabel,
    });
    return confirmed && this.run('archive');
  }

  unschedule(): Promise<boolean> {
    return this.run('unschedule');
  }

  /** The `schedule` command: the only one with a body (ADR 0036). */
  schedule(scheduledFor: string): Promise<boolean> {
    return this.send('schedule', (record) =>
      this.host.api.schedule(record.id, scheduledFor, record.etag),
    );
  }

  /**
   * Deletes the record (ADR 0052). Offered while the form is dirty — asking someone to save a record before
   * throwing it away would be absurd — and the dialogs say so; a record Square holds is asked twice, because that
   * half is not undone by a restore.
   */
  async remove(): Promise<void> {
    const record = this.host.record();
    if (!record || this.host.busy()) return;

    const noun = this.copy.deleteNoun ?? this.copy.noun;
    const confirmed = await confirmDelete(this.confirm, {
      noun,
      label: this.host.label(record),
      slug: record.slug,
      live: record.status === 'Published',
      dirty: this.host.dirty(),
      inSquare: this.copy.inSquare,
      keepLabel: this.copy.keepLabel,
    });
    if (!confirmed) return;

    this.host.clearErrors();
    this.pending.set('delete');
    this.host.announce('Deleting…');
    try {
      await firstValueFrom(this.host.api.delete(record.id, record.etag));
      // Not busy any more before leaving, so the unsaved-changes guard has nothing to ask about on the way out.
      this.pending.set(null);
      await this.host.deleted(record);
    } catch (error: unknown) {
      const problem = problemOf(error);
      this.pending.set(null);
      // cms.in_use names the records that still name this one, which is exactly what the editor needs to read,
      // so its detail is shown as it came (ADR 0052).
      this.host.refused(
        problem?.code === CONTENT_CODES.concurrencyConflict
          ? `Someone else changed this ${noun} a moment ago. Reload it, check it, and try again.`
          : problem?.code === CONTENT_CODES.inUse
            ? (problem.detail ??
              `Another record still names this ${noun}. Take it off that record first.`)
            : messageOf(problem, `The ${noun} couldn’t be deleted. Try again.`),
        null,
      );
    }
  }

  private run(command: Exclude<ContentCommand, 'schedule'>): Promise<boolean> {
    return this.send(command, (record) => this.host.api.command(record.id, command, record.etag));
  }

  private async send(
    command: ContentCommand,
    request: (record: T) => Observable<T>,
  ): Promise<boolean> {
    const record = this.host.record();
    if (!record || this.host.busy() || this.host.dirty()) return false;

    this.host.clearErrors();
    this.pending.set(command);
    this.host.announce(busyLabel(command));
    try {
      const updated = await firstValueFrom(request(record));
      this.host.replace(updated);
      this.pending.set(null);
      this.host.announce(doneLabel(command, this.copy.noun));
      return true;
    } catch (error: unknown) {
      await this.failed(error);
      return false;
    }
  }

  /**
   * A refused command. The form holds no unsaved edits (commands wait for a save), so the latest saved version is
   * simply shown again with the reason — a conflict, a status someone else changed, or what publishing still needs.
   */
  private async failed(error: unknown): Promise<void> {
    const problem = problemOf(error);
    const stale =
      problem?.code === CONTENT_CODES.concurrencyConflict ||
      problem?.code === CONTENT_CODES.transitionInvalid;

    const record = this.host.record();
    if (stale && record) {
      try {
        this.host.replace(await firstValueFrom(this.host.api.get(record.id)));
      } catch {
        // The message below still says what happened.
      }
    }

    this.pending.set(null);
    this.host.refused(
      problem?.code === CONTENT_CODES.concurrencyConflict
        ? `Someone else changed this ${this.copy.noun} a moment ago. Here is the latest version; check it and try again.`
        : messageOf(problem, 'That didn’t work. Try again.'),
      problem,
    );
  }
}

function busyLabel(command: ContentCommand): string {
  switch (command) {
    case 'publish':
      return 'Publishing…';
    case 'unpublish':
      return 'Unpublishing…';
    case 'archive':
      return 'Archiving…';
    case 'schedule':
      return 'Scheduling…';
    case 'unschedule':
      return 'Unscheduling…';
  }
}

function doneLabel(command: ContentCommand, noun: string): string {
  switch (command) {
    case 'publish':
      return 'Published.';
    case 'unpublish':
      return `Unpublished. The ${noun} is a draft again.`;
    case 'archive':
      return 'Archived.';
    case 'schedule':
      return 'Scheduled.';
    case 'unschedule':
      return `Unscheduled. The ${noun} is a draft again.`;
  }
}
