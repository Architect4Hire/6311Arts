import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import { UiButton, UiStatusChip } from '@6311/ui';
import { PublishStatus, STATUS_CHIPS } from '../../../models/content';
import {
  PickerCandidate,
  RecordSearch,
  RecordSearchFn,
  SearchWords,
} from '../record-search/record-search';

/** The record a picker names, as little of it as the picker needs to show. */
export interface PickedRecord {
  readonly id: string;
  readonly name: string;
  readonly status: PublishStatus;
}

/** How a picker talks about what it picks: its search's words, and what it says with nothing picked. */
export interface PickerWords extends SearchWords {
  /** What shows with nothing picked: "Nobody credited yet." */
  readonly none: string;
  /** Read out when nothing is left picked and there was no name to say: "Credit removed." */
  readonly cleared: string;
}

/** Numbers each picker on the page, so a screen with several (one per class run) keeps its ids unique. */
let nextPicker = 0;

/**
 * The one record another names (an artist credited on a piece, the instructor of a class run): who it is now, a
 * `<cms-record-search>` to change it, and a way to take it off. One already named stays shown, with its status,
 * until someone changes it.
 */
@Component({
  selector: 'cms-search-picker',
  imports: [RecordSearch, UiButton, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-picker.html',
  styleUrl: './search-picker.scss',
})
export class SearchPicker {
  /** The record named, or `null`. Two-way: every change is the editor's draft. */
  readonly picked = model.required<PickedRecord | null>();
  readonly words = input.required<PickerWords>();
  readonly search = input.required<RecordSearchFn>();
  readonly heading = input.required<string>();
  readonly disabled = input(false);
  /** The API's or the form's word on the choice, shown above the search. */
  readonly error = input<string | null>(null);
  readonly searchMaxLength = input(100);

  private readonly injector = inject(Injector);
  private readonly finder = viewChild.required(RecordSearch);

  protected readonly chips = STATUS_CHIPS;
  protected readonly headingId = `cms-picker-${++nextPicker}`;
  protected readonly liveMessage = signal('');
  protected readonly named = computed(() => {
    const picked = this.picked();
    return picked ? [picked.id] : [];
  });
  protected readonly label = computed(() => {
    const words = this.words();
    return this.picked() ? `${words.verb} someone else` : `${words.verb} an ${words.noun}`;
  });

  protected choose(candidate: PickerCandidate): void {
    this.picked.set({ id: candidate.id, name: candidate.name, status: candidate.status });
    this.announce(`${candidate.name} ${this.words().done}.`);
    this.focusSearch();
  }

  protected clear(): void {
    const name = this.picked()?.name;
    this.picked.set(null);
    this.announce(name ? `${name} removed.` : this.words().cleared);

    // The Remove button went with what it removed.
    this.focusSearch();
  }

  private focusSearch(): void {
    afterNextRender(() => this.finder().focus(), { injector: this.injector });
  }

  private announce(message: string): void {
    this.liveMessage.update((current) => (current === message ? `${message} ` : message));
  }
}
