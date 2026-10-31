import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField } from '@6311/ui';
import { PublishStatus } from '../../../models/content';

/** A search match: the record, and the line that tells two of the same name apart. */
export interface PickerCandidate {
  readonly id: string;
  readonly name: string;
  readonly slug?: string;
  readonly status: PublishStatus;
  readonly medium?: string | null;
}

/** How a search talks about what it finds; everything else about searching is the same. */
export interface SearchWords {
  /** "artist", "instructor". */
  readonly noun: string;
  readonly plural: string;
  /** What taking a match does, as its button says it: "Credit", "Choose", "Add". */
  readonly verb: string;
  /** What a record is once taken: "credited", "chosen". */
  readonly done: string;
  /** What is searched, as a sentence names it: "the roster", "the instructors". */
  readonly where: string;
  /** How a list of matches is put: "to credit", "to choose from". */
  readonly offered: string;
  /** "No artist on the roster matches", before the search in quotes. */
  readonly noMatch: string;
  /** The search field's name. */
  readonly field: string;
}

/** How a search asks for matches: the type's own list, by name. */
export type RecordSearchFn = (
  search: string,
  limit: number,
) => Observable<{ readonly items: readonly PickerCandidate[] }>;

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300;

/** How many matches to offer at once: enough to find a name, few enough to read. */
const RESULT_LIMIT = 8;

/**
 * A search for records to name — artists to credit, an instructor to choose — with one button per match. Archived
 * records and those already named are not offered: archive is how a record is retired, and a retired one is given
 * no new work (ADR 0033, 0043). What taking one means is the screen's: it hears `chosen`.
 */
@Component({
  selector: 'cms-record-search',
  imports: [UiButton, UiControl, UiField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './record-search.html',
  styleUrl: './record-search.scss',
})
export class RecordSearch {
  readonly words = input.required<SearchWords>();
  readonly search = input.required<RecordSearchFn>();
  readonly label = input.required<string>();
  /** The records already named, which are not offered again. */
  readonly exclude = input<readonly string[]>([]);
  readonly disabled = input(false);
  readonly error = input<string | null>(null);
  readonly maxLength = input(100);
  readonly chosen = output<PickerCandidate>();

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('searchInput');
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bumped by every search, so an answer to an older one is dropped. */
  private generation = 0;

  protected readonly typed = signal('');
  /** The search as last sent. */
  protected readonly query = signal('');
  protected readonly state = signal<'idle' | 'searching' | 'failed' | 'ready'>('idle');
  private readonly results = signal<readonly PickerCandidate[]>([]);
  protected readonly liveMessage = signal('');

  /** What is searched, at the start of a sentence: "The roster". */
  protected readonly whereFirst = computed(() => {
    const where = this.words().where;
    return where.charAt(0).toUpperCase() + where.slice(1);
  });

  /** Matches that can be taken: not archived, and not named already. */
  protected readonly offers = computed(() => {
    const named = new Set(this.exclude());
    return this.results().filter(
      (candidate) => candidate.status !== 'Archived' && !named.has(candidate.id),
    );
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  /** Where adding continues once a match is taken: the button pressed has gone with its offer. */
  focus(): void {
    this.input().nativeElement.focus();
  }

  protected onSearch(value: string): void {
    this.typed.set(value);
    this.clearSearchTimer();
    if (value.trim() === '') {
      this.generation++;
      this.query.set('');
      this.state.set('idle');
      return;
    }
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
      void this.runSearch();
    }, SEARCH_DELAY_MS);
  }

  protected async runSearch(): Promise<void> {
    const search = this.query();
    if (search === '') return;

    const generation = ++this.generation;
    this.state.set('searching');
    try {
      const page = await firstValueFrom(this.search()(search, RESULT_LIMIT));
      if (generation !== this.generation) return;
      this.results.set(page.items);
      this.state.set('ready');
      const words = this.words();
      const count = this.offers().length;
      this.liveMessage.set(
        count === 0
          ? `No ${words.plural} match.`
          : `${count} ${count === 1 ? words.noun : words.plural} ${words.offered}.`,
      );
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
