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
import { InstructorSummary } from '../models/instructor';
import { STATUS_CHIPS } from '../models/content';
import { InstructorService } from '../services/instructor.service';

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
  template: `
    <section class="panel" [attr.aria-labelledby]="headingId">
      <h2 [id]="headingId" class="panel__title">{{ heading() }}</h2>

      <!-- Exists from the start, so what is written into it later is read out. -->
      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

      @if (instructor(); as credited) {
        <p class="credited" data-instructor>
          <span class="credited__name">{{ credited.name }}</span>
          @if (credited.status !== 'Published') {
            <ui-status-chip [tone]="chips[credited.status].tone" [icon]="chips[credited.status].icon">{{
              chips[credited.status].label
            }}</ui-status-chip>
          }
        </p>
        <div class="actions">
          <button
            uiButton
            type="button"
            size="sm"
            data-action="remove-instructor"
            [disabled]="disabled()"
            [attr.aria-label]="'Remove ' + credited.name"
            (click)="clear()"
          >
            Remove
          </button>
        </div>
      } @else {
        <p class="panel__empty">No instructor yet.</p>
      }

      <ui-field
        [label]="instructor() ? 'Choose someone else' : 'Choose an instructor'"
        hint="Search the instructors by name. Archived instructors can’t be chosen."
        [error]="error()"
      >
        <input
          #searchInput
          uiControl
          type="search"
          name="instructorSearch"
          autocomplete="off"
          [attr.maxlength]="searchMaxLength()"
          [disabled]="disabled()"
          [value]="search()"
          (input)="onSearch(searchInput.value)"
        />
      </ui-field>

      @switch (state()) {
        @case ('searching') {
          <p role="status" class="panel__hint">Searching the instructors…</p>
        }
        @case ('failed') {
          <p class="error">
            The instructors couldn’t be searched.
            <button uiButton type="button" size="sm" (click)="runSearch()">Try again</button>
          </p>
        }
        @case ('ready') {
          @if (offers().length === 0) {
            <p class="panel__hint">No instructor matches “{{ query() }}”.</p>
          } @else {
            <ul class="offers" aria-label="Matching instructors">
              @for (candidate of offers(); track candidate.id) {
                <li class="offer">
                  <button
                    uiButton
                    type="button"
                    size="sm"
                    [disabled]="disabled()"
                    [attr.aria-label]="'Choose ' + candidate.name"
                    (click)="choose(candidate)"
                  >
                    Choose
                  </button>
                  <span>
                    {{ candidate.name }}
                    @if (candidate.medium) {
                      <span class="offer__medium">{{ candidate.medium }}</span>
                    }
                  </span>
                </li>
              }
            </ul>
          }
        }
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
    .credited {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      margin: 0;
    }
    .credited__name {
      font-weight: var(--fw-bold);
      overflow-wrap: anywhere;
    }
    .offers {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .offer {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
    }
    .offer__medium {
      display: block;
      font-size: var(--fs-small);
      color: var(--text-soft);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .error {
      margin: 0;
      color: var(--danger);
      font-weight: var(--fw-bold);
    }
  `,
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
