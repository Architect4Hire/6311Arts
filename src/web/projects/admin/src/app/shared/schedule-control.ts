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
import { GALLERY_TIME_ZONE, instantOf, wallClockOf } from '../core/time/gallery-time';
import { PublishStatus } from '../models/content';

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
  template: `
    <!-- Rendered up front, so a screen reader announces what changes inside it (design-system.md). -->
    <p class="schedule__status" role="status">{{ announcement() }}</p>

    @if (canSchedule()) {
      <div class="pair">
        <ui-field label="Day" required>
          <input
            uiControl
            type="date"
            name="scheduleDate"
            [min]="today()"
            [attr.aria-describedby]="describedBy()"
            [attr.aria-invalid]="error() ? 'true' : null"
            [disabled]="disabled()"
            [ngModel]="when().date"
            (ngModelChange)="patch({ date: $event })"
            #dayInput
          />
        </ui-field>
        <ui-field label="Time" hint="At the gallery, Chicago time." required>
          <input
            uiControl
            type="time"
            name="scheduleTime"
            [attr.aria-describedby]="describedBy()"
            [attr.aria-invalid]="error() ? 'true' : null"
            [disabled]="disabled()"
            [ngModel]="when().time"
            (ngModelChange)="patch({ time: $event })"
            #timeInput
          />
        </ui-field>
      </div>

      <!-- One message for the pair, because either field can be the one at fault. -->
      @if (error(); as message) {
        <p class="schedule__error" [id]="errorId" role="alert">{{ message }}</p>
      }

      @if (disabled()) {
        <p class="schedule__hint" [id]="hintId">
          Save your changes first: scheduling acts on the saved version.
        </p>
      }

      <div class="actions">
        <button
          uiButton
          type="button"
          size="sm"
          [attr.aria-describedby]="disabled() ? hintId : null"
          [busy]="busy() === 'schedule'"
          [disabled]="disabled()"
          (click)="submit()"
        >
          {{ status() === 'Scheduled' ? 'Change the time' : 'Schedule' }}
        </button>

        @if (status() === 'Scheduled') {
          <button
            uiButton
            type="button"
            size="sm"
            [attr.aria-describedby]="disabled() ? hintId : null"
            [busy]="busy() === 'unschedule'"
            [disabled]="disabled()"
            (click)="unschedule.emit()"
          >
            Unschedule
          </button>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
    }
    .schedule__status,
    .schedule__hint {
      margin: 0;
      font-size: var(--fs-small);
    }
    .schedule__status:empty {
      display: none;
    }
    .schedule__error {
      display: flex;
      gap: var(--space-1);
      margin: 0;
      color: var(--ink);
      font-size: var(--fs-small);
      font-weight: 600;
    }
    .pair {
      display: grid;
      gap: var(--space-2);
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
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
