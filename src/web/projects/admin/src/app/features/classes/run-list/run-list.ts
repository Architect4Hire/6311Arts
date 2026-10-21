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
import { UiButton, UiControl, UiField, UiStatusChip } from '@6311/ui';
import { GALLERY_TIME_ZONE } from '../../../core/time/gallery-time';
import {
  CLASS_LIMITS,
  ClassRun,
  ClassRunInput,
  isSku,
  normalizeSku,
  suggestedSku,
} from '../../../models/class';
import {
  InstructorPicker,
  PickedInstructor,
} from '../../../shared/pickers/instructor-picker/instructor-picker';
import {
  SessionDraft,
  SessionGrid,
  newSessionDraft,
  sessionDraftsOf,
  sessionErrorOf,
  sessionInputsOf,
  sessionsEqual,
} from '../session-grid/session-grid';

/**
 * One run as the editor holds it (ADR 0044): what staff type, as typed, plus what Square reports about the saved
 * run, which is shown and never sent back (square.md, ADR 0032).
 */
export interface RunDraft {
  /** A stable key, so re-ordering never re-creates a card the person is typing in. */
  readonly key: string;
  /** The saved run this card is, or `null` for one being added. */
  readonly id: string | null;
  readonly label: string;
  readonly instructor: PickedInstructor | null;
  readonly cadence: string;
  readonly sku: string;
  /** As typed, so a half-finished number is not rounded under the person's hands. */
  readonly capacity: string;
  /** The starting price in whole currency units, as typed; empty when there is none. */
  readonly price: string;
  readonly displayTimeZone: string;
  readonly sessions: readonly SessionDraft[];
  /** `false` once Square has reported money or seats: the price is then Square's (ADR 0037). */
  readonly startingPriceEditable: boolean;
  readonly priceCents: number | null;
  readonly seatsRemaining: number | null;
  readonly soldOut: boolean;
  /** Square holds this run: it can't be removed here, because it may have sales. */
  readonly squareHasRecord: boolean;
}

let nextKey = 0;

/** The saved runs as cards, in the order the API returns them (by first session). */
export function runDraftsOf(runs: readonly ClassRun[]): RunDraft[] {
  return runs.map((run) => {
    const zone = run.displayTimeZone || GALLERY_TIME_ZONE;
    return {
      key: `saved-${run.id}`,
      id: run.id,
      label: run.label ?? '',
      instructor: run.instructor
        ? {
            id: run.instructor.id,
            name: run.instructor.name,
            status: run.instructor.status,
          }
        : null,
      cadence: run.cadence ?? '',
      sku: run.sku,
      capacity: String(run.capacity),
      price: run.startingPriceCents === null ? '' : centsToPrice(run.startingPriceCents),
      displayTimeZone: zone,
      sessions: sessionDraftsOf(run.sessions, zone),
      startingPriceEditable: run.startingPriceEditable,
      priceCents: run.priceCents,
      seatsRemaining: run.seatsRemaining,
      soldOut: run.soldOut,
      squareHasRecord: run.squareHasRecord,
    };
  });
}

/**
 * A new run, ready to be filled: the last run's instructor, cadence, seats and price carried over (the next term is
 * usually the same class again), with one empty session a week after the last run's, and a free suggested SKU.
 */
export function newRunDraft(runs: readonly RunDraft[], offeringNumber: number | null): RunDraft {
  const last = runs[runs.length - 1];
  return {
    key: `new-${++nextKey}`,
    id: null,
    label: '',
    instructor: last?.instructor ?? null,
    cadence: last?.cadence ?? '',
    sku: offeringNumber === null ? '' : nextFreeSku(runs, offeringNumber),
    capacity: last?.capacity ?? '',
    price: last?.price ?? '',
    displayTimeZone: last?.displayTimeZone ?? GALLERY_TIME_ZONE,
    sessions: [newSessionDraft(last?.sessions.slice(-1) ?? [])],
    startingPriceEditable: true,
    priceCents: null,
    seatsRemaining: null,
    soldOut: false,
    squareHasRecord: false,
  };
}

/**
 * The cards as the API takes them, or `null` when one can't be sent yet (a session half filled in, a capacity or
 * price that is not a number), so the form says so rather than sending something nobody meant. A run Square holds
 * sends no price: it is Square's now, and the API refuses a different one (ADR 0037).
 */
export function runInputsOf(runs: readonly RunDraft[]): ClassRunInput[] | null {
  const inputs: ClassRunInput[] = [];
  for (const run of runs) {
    const capacity = Number(run.capacity);
    const cents = priceToCents(run.price);
    const sessions = sessionInputsOf(run.sessions, run.displayTimeZone);
    if (run.capacity.trim() === '' || !Number.isSafeInteger(capacity) || sessions === null)
      return null;
    if (run.price.trim() !== '' && cents === null) return null;

    inputs.push({
      capacity,
      ...(run.id ? { id: run.id } : {}),
      ...(run.label.trim() ? { label: run.label.trim() } : {}),
      ...(run.instructor ? { instructorId: run.instructor.id } : {}),
      ...(run.cadence.trim() ? { cadence: run.cadence.trim() } : {}),
      ...(run.sku.trim() ? { sku: normalizeSku(run.sku) } : {}),
      displayTimeZone: run.displayTimeZone,
      ...(run.startingPriceEditable && cents !== null ? { startingPriceCents: cents } : {}),
      sessions,
    });
  }
  return inputs;
}

/** Whether two sets of cards say the same thing, so untouched runs are not sent as a change. */
export function runsEqual(a: readonly RunDraft[], b: readonly RunDraft[]): boolean {
  if (a.length !== b.length) return false;
  return a.every((run, index) => {
    const other = b[index];
    return (
      run.id === other.id &&
      run.label.trim() === other.label.trim() &&
      run.instructor?.id === other.instructor?.id &&
      run.cadence.trim() === other.cadence.trim() &&
      normalizeSku(run.sku) === normalizeSku(other.sku) &&
      run.capacity.trim() === other.capacity.trim() &&
      run.price.trim() === other.price.trim() &&
      run.displayTimeZone === other.displayTimeZone &&
      sessionsEqual(run.sessions, other.sessions)
    );
  });
}

/** What the cards can say before the API does: the first problem, naming the run it is in. */
export function runErrorOf(runs: readonly RunDraft[]): string | null {
  if (runs.length > CLASS_LIMITS.runsMaxCount)
    return `A class has at most ${CLASS_LIMITS.runsMaxCount} runs.`;

  const skus = new Set<string>();
  for (const [index, run] of runs.entries()) {
    const which = runName(run, index);
    const capacity = Number(run.capacity);
    if (
      run.capacity.trim() === '' ||
      !Number.isSafeInteger(capacity) ||
      capacity < 1 ||
      capacity > CLASS_LIMITS.capacityMaxSeats
    ) {
      return `${which} needs a number of seats, from 1 to ${CLASS_LIMITS.capacityMaxSeats}.`;
    }
    if (run.startingPriceEditable && run.price.trim() !== '') {
      const cents = priceToCents(run.price);
      if (cents === null || cents > CLASS_LIMITS.startingPriceMaxCents) {
        return `${which}’s price is a number up to ${CLASS_LIMITS.startingPriceMaxCents / 100}, for example 300 or 37.50.`;
      }
    }
    if (run.sku.trim() !== '') {
      const sku = normalizeSku(run.sku);
      if (!isSku(sku))
        return `${which}’s SKU is letters, digits, hyphens and dots, for example CLS-4-1.`;
      if (skus.has(sku)) return `Two runs have the SKU ${sku}. Each needs its own.`;
      skus.add(sku);
    }
    const sessionError = sessionErrorOf(run.sessions, which);
    if (sessionError) return sessionError;
  }
  return null;
}

/** "Run 2 (Fall 2026)", or "Run 2" before it has a label: how a message and a screen reader name a card. */
export function runName(run: RunDraft, index: number): string {
  return run.label.trim() ? `Run ${index + 1} (${run.label.trim()})` : `Run ${index + 1}`;
}

/** Amounts are integer cents with a currency (api-contract.md). */
export function formatMoney(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

/** Cents as the price field shows them: whole currency units, with cents only when there are any. */
function centsToPrice(cents: number): string {
  return cents % 100 === 0 ? String(cents / 100) : (cents / 100).toFixed(2);
}

/** The price field as integer cents (api-contract.md), or `null` when it is empty or not a number. */
function priceToCents(price: string): number | null {
  const trimmed = price.trim();
  if (trimmed === '') return null;
  if (!/^\d+(?:\.\d{1,2})?$/.test(trimmed)) return null;
  return Math.round(Number(trimmed) * 100);
}

/** The first `CLS-<offering>-<n>` no card is using. The API has the last word on whether it is free. */
function nextFreeSku(runs: readonly RunDraft[], offeringNumber: number): string {
  const taken = new Set(runs.map((run) => normalizeSku(run.sku)));
  for (let number = 1; number <= runs.length + 1; number++) {
    const candidate = suggestedSku(offeringNumber, number);
    if (!taken.has(candidate)) return candidate;
  }
  return suggestedSku(offeringNumber, runs.length + 1);
}

/**
 * A class's runs (`<cms-class-runs [(runs)]>`, ADR 0044): the same class over different terms, each with its own
 * instructor, sessions, seats, SKU and price — all of which the CMS owns — beside what Square reports about it.
 *
 * What Square holds the record of is shown, never edited (square.md, ADR 0032): once Square has reported a run's
 * price, the price field gives way to Square's figure and a **Change price** action that arrives in 5.4a, and the
 * seats left are read-only. A run Square already holds can't be removed here, because it may have sales.
 */
@Component({
  selector: 'cms-class-runs',
  imports: [InstructorPicker, SessionGrid, UiButton, UiControl, UiField, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './run-list.html',
  styleUrl: './run-list.scss',
})
export class ClassRuns {
  /** The runs. Two-way: every change is the editor's draft. */
  readonly runs = model.required<readonly RunDraft[]>();

  /** The class's number, which a new run's suggested SKU is built from; `null` before it is created. */
  readonly offeringNumber = input<number | null>(null);

  /** The currency the API returns amounts in. */
  readonly currency = input('USD');

  readonly disabled = input(false);

  /** The API's or the form's word on the runs, shown under them. */
  readonly error = input<string | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly limits = CLASS_LIMITS;
  protected readonly liveMessage = signal('');
  private readonly id = ++nextKey;
  /** The id the runs' controls point at when the error is theirs. */
  protected readonly errorId = `class-runs-error-${this.id}`;

  protected readonly full = computed(() => this.runs().length >= CLASS_LIMITS.runsMaxCount);

  protected headingId(index: number): string {
    return `class-run-${this.id}-${index}`;
  }

  protected name(run: RunDraft, index: number): string {
    return runName(run, index);
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected money(cents: number): string {
    return formatMoney(cents, this.currency());
  }

  protected patch(index: number, change: Partial<RunDraft>): void {
    this.runs.update((runs) =>
      runs.map((run, position) => (position === index ? { ...run, ...change } : run)),
    );
  }

  protected add(): void {
    const runs = this.runs();
    this.runs.set([...runs, newRunDraft(runs, this.offeringNumber())]);
    this.announce(`Run ${runs.length + 1} added.`);
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>(`[data-run="${runs.length}"] input`)
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected remove(index: number): void {
    const run = this.runs()[index];
    if (!run || run.squareHasRecord) return;
    this.runs.update((runs) => runs.filter((_, position) => position !== index));
    this.announce(`${runName(run, index)} removed.`);

    // The button that was clicked is gone with its card, so focus moves to the card that took its place — or to
    // "Add run" when the last one went, rather than dropping to the top of the document.
    afterNextRender(
      () => {
        const count = this.runs().length;
        const next = this.host.nativeElement.querySelector<HTMLElement>(
          count === 0
            ? '[data-action="add-run"]'
            : `[data-run="${Math.min(index, count - 1)}"] [data-action="remove-run"]`,
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
