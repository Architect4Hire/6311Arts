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
import { UiButton, UiNotice, UiStatusChip } from '@6311/ui';
import { GALLERY_TIME_ZONE, instantOf, wallClockOf } from '../../core/time/gallery-time';
import {
  CLASS_LIMITS,
  ClassSession,
  ClassSessionInput,
  normalizeSku,
  suggestedSku,
} from '../../models/class';

/**
 * One row of the sessions editor: the sitting on the gallery's clock, plus what Square reports about it.
 *
 * The times are a date and two times as staff read them, converted to the instants the API stores only when the
 * form is sent (`gallery-time`). `priceCents`, `seatsRemaining` and `soldOut` are Square's record and are never
 * sent back (square.md, ADR 0032).
 */
export interface SessionDraft {
  /** A stable key for this row, so re-ordering never re-creates an input the person is typing in. */
  readonly key: string;
  /** The saved session this row is, or `null` for one being added. */
  readonly id: string | null;
  readonly date: string;
  readonly starts: string;
  readonly ends: string;
  readonly sku: string;
  /** As typed, so a half-finished number is not rounded under the person's hands. */
  readonly capacity: string;
  readonly displayTimeZone: string;
  readonly priceCents: number | null;
  readonly seatsRemaining: number | null;
  readonly soldOut: boolean;
  /** Square has reported money or seats: the row cannot be removed, and its price is Square's (ADR 0037). */
  readonly squareHasRecord: boolean;
}

let nextKey = 0;

/** The saved sittings as rows, in start order. */
export function sessionDraftsOf(sessions: readonly ClassSession[]): SessionDraft[] {
  return sessions.map((session) => {
    const zone = session.displayTimeZone || GALLERY_TIME_ZONE;
    const starts = wallClockOf(session.startsAt, zone);
    const ends = wallClockOf(session.endsAt, zone);
    return {
      key: `saved-${session.id}`,
      id: session.id,
      date: starts?.date ?? '',
      starts: starts?.time ?? '',
      ends: ends?.time ?? '',
      sku: session.sku,
      capacity: String(session.capacity),
      displayTimeZone: zone,
      priceCents: session.priceCents,
      seatsRemaining: session.seatsRemaining,
      soldOut: session.soldOut,
      squareHasRecord: session.squareHasRecord,
    };
  });
}

/** An empty row, ready to be filled: a week after the last sitting, at the same time, with the same seats. */
export function newSessionDraft(
  rows: readonly SessionDraft[],
  offeringNumber: number | null,
): SessionDraft {
  const last = rows[rows.length - 1];
  const date = last?.date ? addWeek(last.date) : '';
  return {
    key: `new-${++nextKey}`,
    id: null,
    date,
    starts: last?.starts ?? '',
    ends: last?.ends ?? '',
    sku: offeringNumber === null ? '' : nextFreeSku(rows, offeringNumber),
    capacity: last?.capacity ?? '',
    displayTimeZone: last?.displayTimeZone ?? GALLERY_TIME_ZONE,
    priceCents: null,
    seatsRemaining: null,
    soldOut: false,
    squareHasRecord: false,
  };
}

/**
 * The rows as the API takes them. `null` when a row is incomplete or its date and time are not a real instant, so
 * the form says so rather than sending a sitting nobody meant.
 */
export function sessionInputsOf(rows: readonly SessionDraft[]): ClassSessionInput[] | null {
  const inputs: ClassSessionInput[] = [];
  for (const row of rows) {
    const startsAt = instantOf(row.date, row.starts, row.displayTimeZone);
    const endsAt = instantOf(row.date, row.ends, row.displayTimeZone);
    const capacity = Number(row.capacity);
    if (startsAt === null || endsAt === null || !Number.isSafeInteger(capacity)) return null;

    inputs.push({
      startsAt,
      endsAt,
      capacity,
      ...(row.id ? { id: row.id } : {}),
      ...(row.sku.trim() ? { sku: normalizeSku(row.sku) } : {}),
      displayTimeZone: row.displayTimeZone,
    });
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
      row.ends === other.ends &&
      normalizeSku(row.sku) === normalizeSku(other.sku) &&
      row.capacity.trim() === other.capacity.trim() &&
      row.displayTimeZone === other.displayTimeZone
    );
  });
}

/** Amounts are integer cents with a currency (api-contract.md). */
export function formatMoney(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

function addWeek(date: string): string {
  const ms = Date.parse(`${date}T00:00:00Z`);
  if (Number.isNaN(ms)) return '';
  return new Date(ms + 7 * 86_400_000).toISOString().slice(0, 10);
}

/** The first `CLS-<offering>-<n>` no row in the grid is using. The API has the last word on whether it is free. */
function nextFreeSku(rows: readonly SessionDraft[], offeringNumber: number): string {
  const taken = new Set(rows.map((row) => normalizeSku(row.sku)));
  for (let number = 1; number <= rows.length + 1; number++) {
    const candidate = suggestedSku(offeringNumber, number);
    if (!taken.has(candidate)) return candidate;
  }
  return suggestedSku(offeringNumber, rows.length + 1);
}

/**
 * A class's sittings (`<cms-session-grid [(sessions)]>`): when each one meets, the SKU it is sold under and how many
 * seats it has — all of which the CMS owns — beside what Square reports about it.
 *
 * What Square holds the record of is shown, never edited (square.md, ADR 0032): a session's price appears with a
 * **Change price** action that arrives in 5.4a, and the seats left are read-only. A session Square already knows
 * can't be removed here, because it may have sales.
 */
@Component({
  selector: 'cms-session-grid',
  imports: [UiButton, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="sessions-heading">
      <h2 id="sessions-heading" class="panel__title">Sessions</h2>

      <!-- Exists from the start, so what is written into it later is read out. -->
      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

      @if (sessions().length === 0) {
        <p class="panel__empty">No sessions yet. A class needs at least one to be published.</p>
      } @else {
        <div class="scroll" role="region" aria-label="Sessions" tabindex="0">
          <table class="grid">
            <caption class="sr-only">
              Sessions, in the order they meet. Times are {{ zone }} time.
            </caption>
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Starts</th>
                <th scope="col">Ends</th>
                <th scope="col">SKU</th>
                <th scope="col">Seats</th>
                <th scope="col">In Square</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of sessions(); track row.key; let index = $index) {
                <tr [attr.data-session]="index">
                  <td>
                    <input
                      class="field__control"
                      type="date"
                      [attr.name]="'sessionDate' + index"
                      [attr.aria-label]="'Date of session ' + (index + 1)"
                      aria-required="true"
                      [attr.aria-invalid]="error() ? 'true' : null"
                      [attr.aria-describedby]="error() ? errorId : null"
                      [disabled]="disabled()"
                      [value]="row.date"
                      (input)="patch(index, { date: value($event) })"
                    />
                  </td>
                  <td>
                    <input
                      class="field__control"
                      type="time"
                      [attr.name]="'sessionStarts' + index"
                      [attr.aria-label]="'Start time of session ' + (index + 1)"
                      aria-required="true"
                      [attr.aria-invalid]="error() ? 'true' : null"
                      [attr.aria-describedby]="error() ? errorId : null"
                      [disabled]="disabled()"
                      [value]="row.starts"
                      (input)="patch(index, { starts: value($event) })"
                    />
                  </td>
                  <td>
                    <input
                      class="field__control"
                      type="time"
                      [attr.name]="'sessionEnds' + index"
                      [attr.aria-label]="'End time of session ' + (index + 1)"
                      aria-required="true"
                      [attr.aria-invalid]="error() ? 'true' : null"
                      [attr.aria-describedby]="error() ? errorId : null"
                      [disabled]="disabled()"
                      [value]="row.ends"
                      (input)="patch(index, { ends: value($event) })"
                    />
                  </td>
                  <td>
                    <input
                      class="field__control"
                      [attr.name]="'sessionSku' + index"
                      [attr.aria-label]="'SKU of session ' + (index + 1)"
                      [attr.aria-invalid]="error() ? 'true' : null"
                      [attr.aria-describedby]="error() ? errorId : null"
                      autocomplete="off"
                      spellcheck="false"
                      [attr.maxlength]="limits.skuMaxLength"
                      [disabled]="disabled()"
                      [value]="row.sku"
                      (input)="patch(index, { sku: value($event) })"
                    />
                  </td>
                  <td>
                    <input
                      class="field__control seats"
                      type="number"
                      inputmode="numeric"
                      min="1"
                      [attr.max]="limits.capacityMaxSeats"
                      [attr.name]="'sessionCapacity' + index"
                      [attr.aria-label]="'Seats in session ' + (index + 1)"
                      aria-required="true"
                      [attr.aria-invalid]="error() ? 'true' : null"
                      [attr.aria-describedby]="error() ? errorId : null"
                      [disabled]="disabled()"
                      [value]="row.capacity"
                      (input)="patch(index, { capacity: value($event) })"
                    />
                  </td>
                  <td class="square">
                    @if (row.squareHasRecord) {
                      @if (row.priceCents !== null) {
                        <span class="square__price">{{ money(row.priceCents) }}</span>
                      }
                      @if (row.seatsRemaining !== null) {
                        <span class="square__seats"
                          >{{ row.seatsRemaining }} of {{ row.capacity }} seats left</span
                        >
                      }
                      @if (row.soldOut) {
                        <ui-status-chip tone="attention" icon="stop">Sold out</ui-status-chip>
                      }
                      <!-- 5.4a sends the change as an audited command; until then the figure is Square's to say. -->
                      <button
                        uiButton
                        type="button"
                        size="sm"
                        [disabled]="true"
                        [attr.aria-label]="'Change price for session ' + (index + 1)"
                        [attr.aria-describedby]="squareNoticeId"
                      >
                        Change price
                      </button>
                    } @else {
                      <span class="square__none">Not in Square yet</span>
                    }
                  </td>
                  <td class="row-actions">
                    <button
                      uiButton
                      type="button"
                      size="sm"
                      data-action="remove"
                      [disabled]="disabled() || row.squareHasRecord"
                      [attr.aria-label]="'Remove session ' + (index + 1)"
                      (click)="remove(index)"
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (anySynced()) {
        <ui-notice [id]="squareNoticeId" tone="info">
          A session Square already holds can’t be removed here, and its price is Square’s record.
          Archive the class instead of removing the session.
        </ui-notice>
      }

      @if (error(); as message) {
        <p class="error" role="alert" [id]="errorId">{{ message }}</p>
      }

      @if (full()) {
        <p class="panel__hint">
          A class has at most {{ limits.sessionsMaxCount }} sessions. Remove one to add another.
        </p>
      } @else {
        <div class="actions">
          <button
            uiButton
            type="button"
            size="sm"
            data-action="add"
            [disabled]="disabled()"
            (click)="add()"
          >
            Add session
          </button>
          <p class="panel__hint">
            Times are {{ zone }} time. A SKU is suggested; change it if the front desk uses another.
          </p>
        </div>
      }
    </section>
  `,
  styles: `
    .panel {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      border: var(--border-thin) solid var(--line);
    }
    .panel__title {
      margin: 0;
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
    }
    .panel__empty,
    .panel__hint {
      margin: 0;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .scroll {
      overflow-x: auto;
    }
    .grid {
      width: 100%;
      border-collapse: collapse;
    }
    .grid th,
    .grid td {
      padding: var(--space-1) var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      text-align: left;
      vertical-align: top;
    }
    .grid thead th {
      font-size: var(--fs-label);
      font-weight: var(--fw-heavy);
      letter-spacing: var(--track-label);
      text-transform: uppercase;
      white-space: nowrap;
    }
    .square {
      display: grid;
      gap: var(--space-1);
      font-size: var(--fs-small);
    }
    .square__price {
      font-weight: var(--fw-bold);
    }
    .square__seats,
    .square__none {
      color: var(--text-soft);
    }
    .row-actions {
      white-space: nowrap;
    }
    .seats {
      width: 6rem;
    }
    .actions {
      display: grid;
      gap: var(--space-2);
      justify-items: start;
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
  `,
})
export class SessionGrid {
  /** The sittings, in the order they meet. Two-way: every change is the editor's draft. */
  readonly sessions = model.required<readonly SessionDraft[]>();

  /** The class's number, which a new row's suggested SKU is built from; `null` before it is created. */
  readonly offeringNumber = input<number | null>(null);

  /** The currency the API returns amounts in. */
  readonly currency = input('USD');

  readonly disabled = input(false);

  /** The API's or the form's word on the sessions, shown under the grid. */
  readonly error = input<string | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly limits = CLASS_LIMITS;
  protected readonly zone = GALLERY_TIME_ZONE;
  protected readonly liveMessage = signal('');
  /** Ids the cells point at, so a row's inputs are described by the grid's own error and notice. */
  protected readonly errorId = `session-grid-error-${++nextKey}`;
  protected readonly squareNoticeId = `session-grid-square-${nextKey}`;

  protected readonly full = computed(() => this.sessions().length >= CLASS_LIMITS.sessionsMaxCount);

  protected readonly anySynced = computed(() => this.sessions().some((row) => row.squareHasRecord));

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected money(cents: number): string {
    return formatMoney(cents, this.currency());
  }

  protected patch(index: number, change: Partial<SessionDraft>): void {
    this.sessions.update((rows) =>
      rows.map((row, position) => (position === index ? { ...row, ...change } : row)),
    );
  }

  protected add(): void {
    const rows = this.sessions();
    this.sessions.set([...rows, newSessionDraft(rows, this.offeringNumber())]);
    this.announce(`Session ${rows.length + 1} added.`);
  }

  protected remove(index: number): void {
    const row = this.sessions()[index];
    if (!row || row.squareHasRecord) return;
    this.sessions.update((rows) => rows.filter((_, position) => position !== index));
    this.announce(`Session ${index + 1} removed.`);

    // The button that was clicked is gone with its row, so focus moves to the one that took its place — or to
    // "Add session" when the last row went, rather than dropping to the top of the document.
    afterNextRender(
      () => {
        const rows = this.sessions().length;
        const target = rows === 0 ? index : Math.min(index, rows - 1);
        const next = this.host.nativeElement.querySelector<HTMLElement>(
          rows === 0 ? '[data-action="add"]' : `[data-session="${target}"] [data-action="remove"]`,
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
