import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiControl, UiField, UiStatusChip } from '@6311/ui';
import { InstructorSummary } from '../../../models/instructor';
import { STATUS_CHIPS } from '../../../models/content';
import { InstructorService } from '../../../services/instructor.service';

/** How long typing pauses before the instructors are searched. */
const SEARCH_DELAY_MS = 300;

/** How many matches to offer at once: enough to find a name, few enough to read. */
const RESULT_LIMIT = 8;

/** The instructor a record names, as little of them as a picker needs to show. */
export interface PickedInstructor {
  readonly id: string;
  readonly name: string;
  readonly status: InstructorSummary['status'];
}

type SearchState = 'idle' | 'searching' | 'failed' | 'ready';

/** Numbers each picker on the page, so a screen with several (one per class run) keeps its ids unique. */
let nextPicker = 0;

/**
 * The one instructor a record names (`<cms-instructor-picker [(instructor)] heading="Instructor">`): who it is now, a
 * search of the instructors to change it, and a way to take them off. Archived instructors are not offered — archive is
 * how an instructor is retired, and a retired instructor is given no new runs (ADR 0043, 0044). One that is already
 * named stays shown, with their status, until someone changes it.
 *
 * Cloned from `<cms-artist-picker>`; a class editor shows one per run, so its ids are numbered per instance.
 */
@Component({
  selector: 'cms-instructor-picker',
  imports: [UiButton, UiControl, UiField, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './instructor-picker.html',
  styleUrl: './instructor-picker.scss',
})
export class InstructorPicker {
  /** The named instructor, or `null`. Two-way: every change is the editor's draft. */
  readonly instructor = model.required<PickedInstructor | null>();

  /** What the panel is called, so a screen can say "Instructor" or "Who teaches it". */
  readonly heading = input('Instructor');

  readonly disabled = input(false);

  /** The API's or the form's word on the choice, shown above the search. */
  readonly error = input<string | null>(null);

  readonly searchMaxLength = input(100);

  private readonly instructors = inject(InstructorService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bumped by every search, so an answer to an older one is dropped. */
  private generation = 0;

  protected readonly headingId = `instructor-picker-${++nextPicker}`;
  protected readonly chips = STATUS_CHIPS;
  protected readonly search = signal('');
  /** The search as last sent. */
  protected readonly query = signal('');
  protected readonly state = signal<SearchState>('idle');
  private readonly results = signal<readonly InstructorSummary[]>([]);
  protected readonly liveMessage = signal('');

  /** Matches that can be chosen: not archived, and not the one named already. */
  protected readonly offers = computed(() => {
    const credited = this.instructor()?.id;
    return this.results().filter(
      (candidate) => candidate.status !== 'Archived' && candidate.id !== credited,
    );
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearSearchTimer());
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.query.set(value.trim());
      void this.runSearch();
    }, SEARCH_DELAY_MS);
  }

  protected choose(candidate: InstructorSummary): void {
    this.instructor.set({ id: candidate.id, name: candidate.name, status: candidate.status });
    this.announce(`${candidate.name} chosen.`);

    // The button that was clicked is gone — the named instructor is no longer offered — so focus returns to the search.
    this.focusSearch();
  }

  protected clear(): void {
    const name = this.instructor()?.name;
    this.instructor.set(null);
    this.announce(name ? `${name} removed.` : 'Instructor removed.');

    // The Remove button went with the instructor it removed.
    this.focusSearch();
  }

  private focusSearch(): void {
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>('input[name="instructorSearch"]')
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected async runSearch(): Promise<void> {
    const search = this.query();
    if (search === '') {
      this.state.set('idle');
      return;
    }

    const generation = ++this.generation;
    this.state.set('searching');
    try {
      const page = await firstValueFrom(this.instructors.list({ search, limit: RESULT_LIMIT }));
      if (generation !== this.generation) return;
      this.results.set(page.items);
      this.state.set('ready');
      const count = this.offers().length;
      this.announce(
        count === 0
          ? 'No instructors match.'
          : `${count} ${count === 1 ? 'instructor' : 'instructors'} to choose from.`,
      );
    } catch {
      if (generation === this.generation) this.state.set('failed');
    }
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }

  private clearSearchTimer(): void {
    if (this.searchTimer !== null) clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }
}
