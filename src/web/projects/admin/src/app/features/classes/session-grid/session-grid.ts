import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import { UiButton } from '@6311/ui';
import { GALLERY_TIME_ZONE, instantOf, wallClockOf } from '../../../core/time/gallery-time';
import { CLASS_LIMITS, ClassSession, ClassSessionInput } from '../../../models/class';

/**
 * One row of a run's sessions editor: the sitting on the run's clock.
 *
 * The times are a date and two times as staff read them, converted to the instants the API stores only when the
 * form is sent (`gallery-time`). Nothing is sold by the sitting — the seats, the SKU and the price are the run's
 * (ADR 0044) — so a row is only a time.
 */
export interface SessionDraft {
  /** A stable key for this row, so re-ordering never re-creates an input the person is typing in. */
  readonly key: string;
  /** The saved session this row is, or `null` for one being added. */
  readonly id: string | null;
  readonly date: string;
  readonly starts: string;
  readonly ends: string;
}

let nextKey = 0;

/** The saved sittings as rows, in start order, on the clock of `zone`. */
export function sessionDraftsOf(sessions: readonly ClassSession[], zone: string): SessionDraft[] {
  return sessions.map((session) => {
    const starts = wallClockOf(session.startsAt, zone);
    const ends = wallClockOf(session.endsAt, zone);
    return {
      key: `saved-${session.id}`,
      id: session.id,
      date: starts?.date ?? '',
      starts: starts?.time ?? '',
      ends: ends?.time ?? '',
    };
  });
}

/** An empty row, ready to be filled: a week after the last sitting, at the same time. */
export function newSessionDraft(rows: readonly SessionDraft[]): SessionDraft {
  const last = rows[rows.length - 1];
  return {
    key: `new-${++nextKey}`,
    id: null,
    date: last?.date ? addWeek(last.date) : '',
    starts: last?.starts ?? '',
    ends: last?.ends ?? '',
  };
}

/**
 * The rows as the API takes them. `null` when a row is incomplete or its date and time are not a real instant on
 * `zone`'s clock, so the form says so rather than sending a sitting nobody meant.
 */
export function sessionInputsOf(
  rows: readonly SessionDraft[],
  zone: string,
): ClassSessionInput[] | null {
  const inputs: ClassSessionInput[] = [];
  for (const row of rows) {
    const startsAt = instantOf(row.date, row.starts, zone);
    const endsAt = instantOf(row.date, row.ends, zone);
    if (startsAt === null || endsAt === null) return null;
    inputs.push({ startsAt, endsAt, ...(row.id ? { id: row.id } : {}) });
  }
  return inputs;
}

/** Whether two sets of rows say the same thing, so an untouched grid is not sent as a change. */
export function sessionsEqual(a: readonly SessionDraft[], b: readonly SessionDraft[]): boolean {
  if (a.length !== b.length) return false;
  return a.every((row, index) => {
    const other = b[index];
    return (
      row.id === other.id &&
      row.date === other.date &&
      row.starts === other.starts &&
      row.ends === other.ends
    );
  });
}

/** What the grid can say before the API does: a row half filled in, or one that ends before it starts. */
export function sessionErrorOf(rows: readonly SessionDraft[], run: string): string | null {
  if (rows.length > CLASS_LIMITS.sessionsMaxCount) {
    return `${run} has more than ${CLASS_LIMITS.sessionsMaxCount} sessions.`;
  }
  for (const [index, row] of rows.entries()) {
    const which = `${run}, session ${index + 1}`;
    if (!row.date || !row.starts || !row.ends)
      return `${which} needs a date, a start time and an end time.`;
    if (row.ends <= row.starts) return `${which} can’t end before it starts.`;
  }
  return null;
}

function addWeek(date: string): string {
  const ms = Date.parse(`${date}T00:00:00Z`);
  if (Number.isNaN(ms)) return '';
  return new Date(ms + 7 * 86_400_000).toISOString().slice(0, 10);
}

/**
 * One run's sittings (`<cms-session-grid [(sessions)] [run]>`): when each one meets. A seat in the run is a seat in
 * every sitting, so there is nothing per row but the date and the times (ADR 0044).
 */
@Component({
  selector: 'cms-session-grid',
  imports: [UiButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './session-grid.html',
  styleUrl: './session-grid.scss',
})
export class SessionGrid {
  /** The sittings, in the order they meet. Two-way: every change is the editor's draft. */
  readonly sessions = model.required<readonly SessionDraft[]>();

  /** How the run is named to a screen reader ("Run 2"), so each grid's controls say whose they are. */
  readonly run = input('Run 1');

  /** The prefix of each input's name, unique per run, so a spec or the browser can tell the grids apart. */
  readonly prefix = input('session');

  /** The zone the times are typed in: the run's. */
  readonly zone = input(GALLERY_TIME_ZONE);

  readonly disabled = input(false);

  /** Whether the run's error is about these sessions, so the cells are marked and described by it. */
  readonly invalid = input(false);

  /** The id of the run's error message, which marked cells point at. */
  readonly errorId = input<string | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly limits = CLASS_LIMITS;
  protected readonly liveMessage = signal('');

  protected readonly full = computed(() => this.sessions().length >= CLASS_LIMITS.sessionsMaxCount);
  protected readonly describedBy = computed(() => (this.invalid() ? this.errorId() : null));

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected patch(index: number, change: Partial<SessionDraft>): void {
    this.sessions.update((rows) =>
      rows.map((row, position) => (position === index ? { ...row, ...change } : row)),
    );
  }

  protected add(): void {
    const rows = this.sessions();
    this.sessions.set([...rows, newSessionDraft(rows)]);
    this.announce(`${this.run()}: session ${rows.length + 1} added.`);
  }

  protected remove(index: number): void {
    this.sessions.update((rows) => rows.filter((_, position) => position !== index));
    this.announce(`${this.run()}: session ${index + 1} removed.`);

    // The button that was clicked is gone with its row, so focus moves to the one that took its place — or to
    // "Add session" when the last row went, rather than dropping to the top of the document.
    afterNextRender(
      () => {
        const rows = this.sessions().length;
        const target = Math.min(index, rows - 1);
        const next = this.host.nativeElement.querySelector<HTMLElement>(
          rows === 0
            ? '[data-action="add-session"]'
            : `[data-session="${target}"] [data-action="remove-session"]`,
        );
        next?.focus();
      },
      { injector: this.injector },
    );
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }
}
