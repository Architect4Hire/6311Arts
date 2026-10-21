import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Exhibition } from '../../../core/models';
import { WhenPipe } from '../../format';

/** A dated moment in an exhibition's run. */
export type ShowMoment = 'opens' | 'reception' | 'closes';

/**
 * One of an exhibition's dates (opening, reception, last day) set among the classes. It shares the class card's
 * layout so the list lines up, and is told apart by its charcoal fill, its rule and the word "Exhibition".
 */
@Component({
  selector: 'app-show-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, WhenPipe],
  host: { class: 'class-card class-card--show' },
  templateUrl: './show-card.html',
})
export class ShowCard {
  item = input.required<Exhibition>();
  /** The moment's Chicago date (YYYY-MM-DD). */
  date = input.required<string>();
  kind = input.required<ShowMoment>();
  protected label = computed(
    () => ({ opens: 'Opens', reception: 'Opening reception', closes: 'Last day' })[this.kind()],
  );
}
