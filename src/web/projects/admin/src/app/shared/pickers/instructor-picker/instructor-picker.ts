import { ChangeDetectionStrategy, Component, inject, input, model } from '@angular/core';
import { InstructorService } from '../../../services/instructor.service';
import { PickedRecord, PickerWords, SearchPicker } from '../search-picker/search-picker';

/** The instructor a record names, as little of them as a picker needs to show. */
export type PickedInstructor = PickedRecord;

const WORDS: PickerWords = {
  noun: 'instructor',
  plural: 'instructors',
  verb: 'Choose',
  done: 'chosen',
  where: 'the instructors',
  none: 'No instructor yet.',
  cleared: 'Instructor removed.',
  offered: 'to choose from',
  noMatch: 'No instructor matches',
  field: 'instructorSearch',
};

/**
 * The one instructor a record names (`<cms-instructor-picker [(instructor)] heading="Instructor">`): a
 * `<cms-search-picker>` over the instructors. A class editor shows one per run (ADR 0043, 0044).
 */
@Component({
  selector: 'cms-instructor-picker',
  imports: [SearchPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './instructor-picker.html',
})
export class InstructorPicker {
  /** The named instructor, or `null`. Two-way: every change is the editor's draft. */
  readonly instructor = model.required<PickedInstructor | null>();
  /** What the panel is called, so a screen can say "Instructor" or "Who teaches it". */
  readonly heading = input('Instructor');
  readonly disabled = input(false);
  readonly error = input<string | null>(null);
  readonly searchMaxLength = input(100);

  private readonly instructors = inject(InstructorService);

  protected readonly words = WORDS;
  protected readonly search = (search: string, limit: number) =>
    this.instructors.list({ search, limit });
}
