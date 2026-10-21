import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ArtClass } from '../../../core/models';
import { slugify } from '../../../data/mock-shop';
import { MoneyPipe, WhenPipe } from '../../format';

@Component({
  selector: 'app-class-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MoneyPipe, WhenPipe],
  host: { class: 'class-card' },
  templateUrl: './class-card.html',
})
export class ClassCard {
  item = input.required<ArtClass>();
  /** When the card stands for one session of a weekly course: that session's date (YYYY-MM-DD) and week number. */
  date = input<string>();
  week = input<number>();
  /** Classes carry an instructor's name, not an id; the profile page is keyed on the slugified name. */
  protected slug = slugify;
}
