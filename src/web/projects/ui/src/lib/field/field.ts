import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  Directive,
  inject,
  input,
} from '@angular/core';
import { UiIcon } from '../icon/icon';

let nextId = 0;

/**
 * A labelled form field: label, the projected `uiControl`, an optional hint and an optional error.
 * "Required" is a word in the label, as on the spike's forms, never colour or an asterisk alone.
 * Errors are linked through aria-describedby, which is read on focus, not announced: on a failed submit, move
 * focus to the first invalid control (or an error summary) so the message is heard.
 *
 * ```html
 * <ui-field label="Email" hint="We reply within a day." required [error]="emailError()">
 *   <input uiControl type="email" autocomplete="email" [formField]="form.email" />
 * </ui-field>
 * ```
 */
@Component({
  selector: 'ui-field',
  imports: [UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'field' },
  template: `
    <label class="field__label" [attr.for]="id()"
      >{{ label() }}
      @if (required()) {
        <span class="field__req">required</span>
      }
    </label>
    <ng-content />
    @if (hint()) {
      <span class="field__hint" [id]="hintId()">{{ hint() }}</span>
    }
    @if (error()) {
      <span class="field__error" [id]="errorId()"
        ><ui-icon name="stop" [size]="16" [stroke]="2.4" />{{ error() }}</span
      >
    }
  `,
})
export class UiField {
  readonly label = input.required<string>();
  readonly hint = input<string>();
  /** Validation message. Non-empty marks the control invalid and describes it. */
  readonly error = input<string | null>(null);
  readonly required = input(false, { transform: booleanAttribute });
  /** Overrides the generated control id, e.g. when a page needs a stable anchor. */
  readonly controlId = input<string>();

  private readonly autoId = `ui-field-${nextId++}`;
  readonly id = computed(() => this.controlId() || this.autoId);
  protected readonly hintId = computed(() => `${this.id()}-hint`);
  protected readonly errorId = computed(() => `${this.id()}-error`);
  readonly describedBy = computed(
    () =>
      [this.hint() ? this.hintId() : null, this.error() ? this.errorId() : null]
        .filter(Boolean)
        .join(' ') || null,
  );
}

/** Marks the native control inside a `ui-field` and wires its id, description, invalid and required state. */
@Directive({
  selector: 'input[uiControl], select[uiControl], textarea[uiControl]',
  host: {
    class: 'field__control',
    '[id]': 'field.id()',
    '[attr.aria-describedby]': 'field.describedBy()',
    '[attr.aria-invalid]': 'field.error() ? "true" : null',
    '[attr.aria-required]': 'field.required() ? "true" : null',
  },
})
export class UiControl {
  protected readonly field = inject(UiField);
}
