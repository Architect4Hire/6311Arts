import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButton, UiControl, UiField } from '@6311/ui';
import { GALLERY_TIME_ZONE, instantOf, wallClockOf } from '../../core/time/gallery-time';
import { PublishStatus } from '../../models/content';

/** When the record is set to publish, as a clock at the gallery reads it. */
interface When {
  readonly date: string;
  readonly time: string;
}

const EMPTY: When = { date: '', time: '' };

const SHOWN = new Intl.DateTimeFormat('en-US', {
  timeZone: GALLERY_TIME_ZONE,
  dateStyle: 'medium',
  timeStyle: 'short',
});

let nextId = 0;

/**
 * The `schedule` and `unschedule` commands on a content editor's publishing panel (ADR 0036): pick a day and a
 * time, and the Worker publishes the record then.
 *
 * ```html
 * <cms-schedule-control
 *   noun="artist"
 *   [status]="artist.status"
 *   [scheduledFor]="artist.scheduledFor"
 *   [disabled]="dirty() || busy()"
 *   [busy]="commanding()"
 *   (schedule)="schedule($event)"
 *   (unschedule)="unschedule()"
 * />
 * ```
 *
 * Times are the gallery's own clock, as the exhibition reception's are: staff never type UTC. The instant sent is
 * what a clock in Chicago reads at that moment (`instantOf`).
 */
@Component({
  selector: 'cms-schedule-control',
  imports: [FormsModule, UiButton, UiControl, UiField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './schedule-control.html',
  styleUrl: './schedule-control.scss',
})
export class ScheduleControl {
  /** How the record is named in messages, for example "artist". */
  readonly noun = input.required<string>();
  readonly status = input.required<PublishStatus>();
  /** The instant it is scheduled for, as the API sends it, or `null`. */
  readonly scheduledFor = input<string | null>(null);
  /** Unsaved edits, or another command in flight: the panel acts on the saved version. */
  readonly disabled = input(false);
  /** Which command is running, so only its own button spins. */
  readonly busy = input<string | null>(null);

  /** The chosen instant, as the API writes instants. */
  readonly schedule = output<string>();
  readonly unschedule = output<void>();

  protected readonly errorId = `cms-schedule-error-${nextId}`;
  protected readonly hintId = `cms-schedule-hint-${nextId++}`;

  private readonly dayInput = viewChild<ElementRef<HTMLInputElement>>('dayInput');
  private readonly timeInput = viewChild<ElementRef<HTMLInputElement>>('timeInput');

  /**
   * What the fields hold: what was typed, reset whenever the saved time changes. Without the reset, the typed
   * value would survive the schedule it made, an unschedule, and a reload of the record.
   */
  private readonly edited = linkedSignal<string | null, When | null>({
    source: () => this.scheduledFor(),
    computation: () => null,
  });

  protected readonly error = signal<string | null>(null);

  protected readonly when = computed(() => this.edited() ?? this.saved() ?? EMPTY);

  /** Draft and Scheduled only: a published record is unpublished first (PublishLifecycle). */
  protected readonly canSchedule = computed(() => {
    const status = this.status();
    return status === 'Draft' || status === 'Scheduled';
  });

  protected readonly announcement = computed(() => {
    if (this.status() !== 'Scheduled') return '';

    const iso = this.scheduledFor();
    if (!iso) return '';

    const ms = Date.parse(iso);
    return Number.isNaN(ms) ? '' : `Publishing on ${SHOWN.format(ms)} (gallery time).`;
  });

  protected describedBy(): string | null {
    const ids = [this.error() ? this.errorId : null, this.disabled() ? this.hintId : null].filter(
      (id): id is string => id !== null,
    );
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** Read on each render rather than computed once, so it is still today's date after midnight. */
  protected today(): string {
    return wallClockOf(new Date().toISOString())?.date ?? '';
  }

  protected patch(change: Partial<When>): void {
    this.edited.set({ ...this.when(), ...change });
    this.error.set(null);
  }

  protected submit(): void {
    if (this.disabled()) return;

    const { date, time } = this.when();
    if (!date || !time) {
      // Focus follows the message, so a keyboard or screen-reader user lands on the field to fix (ui-field).
      this.refuse(
        'Choose the day and the time to publish.',
        date ? this.timeInput() : this.dayInput(),
      );
      return;
    }

    const instant = instantOf(date, time);
    if (!instant) {
      this.refuse('That is not a day and time.', this.dayInput());
      return;
    }

    // The API refuses a time already past as well; saying so here saves a round trip (ADR 0036).
    if (Date.parse(instant) <= Date.now()) {
      this.refuse(
        `That time has passed. Pick a later one, or publish the ${this.noun()} now.`,
        this.timeInput(),
      );
      return;
    }

    this.error.set(null);
    this.schedule.emit(instant);
  }

  private refuse(message: string, focus: ElementRef<HTMLInputElement> | undefined): void {
    this.error.set(message);
    focus?.nativeElement.focus();
  }

  /** The saved time as the fields show it, so "change the time" starts from what is set. */
  private readonly saved = computed(() => {
    const iso = this.scheduledFor();
    return iso ? wallClockOf(iso) : null;
  });
}
