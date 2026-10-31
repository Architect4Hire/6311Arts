import { Signal, computed, inject, linkedSignal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

export type SortDirection = 'asc' | 'desc';

/** How a column reads each way round: the words that announce the order and that the phone picker offers. */
export type SortKind = 'text' | 'date' | 'number' | 'status' | 'flag';

/** One column a CMS list can be sorted by (ADR 0062). */
export interface SortColumn {
  /** The API's name for it, as `sort` sends it. */
  readonly key: string;
  /** The header's own words. */
  readonly label: string;
  readonly kind: SortKind;
  /** The order a first click gives. Dates start newest first; everything else from the top. */
  readonly first?: SortDirection;
  /** Words for each way round, where the kind's own would not fit (a status that is not the content lifecycle). */
  readonly words?: Readonly<Record<SortDirection, string>>;
}

export interface SortState {
  readonly column: string;
  readonly direction: SortDirection;
}

const WORDS: Readonly<Record<SortKind, Readonly<Record<SortDirection, string>>>> = {
  text: { asc: 'A to Z', desc: 'Z to A' },
  date: { asc: 'oldest first', desc: 'newest first' },
  number: { asc: 'lowest first', desc: 'highest first' },
  status: { asc: 'drafts first', desc: 'archived first' },
  flag: { asc: 'no first', desc: 'yes first' },
};

/** The API's `sort` for a state: `name`, or `-updated` for descending. */
export function sortParam(state: SortState): string {
  return state.direction === 'desc' ? `-${state.column}` : state.column;
}

const same = (a: SortState, b: SortState) => a.column === b.column && a.direction === b.direction;

/**
 * A list's sort (ADR 0062): which column, which way, and the words for it. It lives in the address bar as `?sort=`,
 * so returning from a record with Back finds the list in the order it was left, and a link to a sorted list opens
 * sorted. The list's own order (the API's default when `sort` is absent) stays out of the address.
 *
 * Call it from a component's field initialiser: it injects the route.
 */
export class ListSort {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly query = toSignal(this.route.queryParamMap);

  private readonly state = linkedSignal({
    source: () => this.query()?.get('sort') ?? null,
    computation: (text: string | null): SortState => this.parse(text),
    equal: same,
  });

  /**
   * @param columns The columns a header can sort by.
   * @param fallback The list's own order, which may be a column no header shows (a membership tier's display order).
   * @param fallbackLabel How to name {@link fallback} when no column does.
   */
  constructor(
    readonly columns: readonly SortColumn[],
    readonly fallback: SortState,
    private readonly fallbackLabel?: string,
  ) {}

  readonly current: Signal<SortState> = this.state.asReadonly();

  /** What `sort` sends for {@link current}. */
  readonly param = computed(() => sortParam(this.state()));

  /** "Updated, newest first": for the caption, the live region and the phone picker. */
  readonly description = computed(() => this.describe(this.state()));

  /** The direction {@link column} is sorted in, or `null` when the list is on another column. */
  directionOf(column: string): SortDirection | null {
    const state = this.state();
    return state.column === column ? state.direction : null;
  }

  /** The order a first click on {@link column} gives. */
  firstDirection(column: string): SortDirection {
    const found = this.column(column);
    return found?.first ?? (found?.kind === 'date' ? 'desc' : 'asc');
  }

  /** A header's click: the other way round on the column already sorted, else that column's first order. */
  toggle(column: string): void {
    const direction = this.directionOf(column);
    this.set({
      column,
      direction:
        direction === null ? this.firstDirection(column) : direction === 'asc' ? 'desc' : 'asc',
    });
  }

  set(state: SortState): void {
    if (same(state, this.state())) return;
    this.state.set(state);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sort: same(state, this.fallback) ? null : sortParam(state) },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  describe(state: SortState): string {
    const found = this.column(state.column);
    if (!found) return this.fallbackLabel ?? '';
    return `${found.label}, ${(found.words ?? WORDS[found.kind])[state.direction]}`;
  }

  /**
   * "Sorted by Updated, newest first. " when a list has just loaded in an order other than the one it last
   * announced, for its live region to say before the count; empty on the first load and when the order is unchanged.
   */
  announcement(): string {
    const param = this.param();
    const changed = this.announced !== null && this.announced !== param;
    this.announced = param;
    return changed ? `Sorted by ${this.description()}. ` : '';
  }

  private announced: string | null = null;

  /** `?sort=` read back, anything this list doesn't sort by being its own order. */
  private parse(text: string | null): SortState {
    if (!text) return this.fallback;
    const direction: SortDirection = text.startsWith('-') ? 'desc' : 'asc';
    const column = direction === 'desc' ? text.slice(1) : text;
    return this.column(column) || column === this.fallback.column
      ? { column, direction }
      : this.fallback;
  }

  private column(key: string): SortColumn | undefined {
    return this.columns.find((candidate) => candidate.key === key);
  }
}
