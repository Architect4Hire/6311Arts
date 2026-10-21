import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ArtClass, Exhibition, Product } from '../core/models';
import { slugify } from '../data/mock-shop';
import { ArtImage } from './art-image';
import { MoneyPipe, WhenPipe, today } from './format';

@Component({
  selector: 'app-class-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MoneyPipe, WhenPipe],
  host: { class: 'class-card' },
  template: `
    @let c = item();
    @let on = date() ?? c.start;
    <a class="class-card__date" [routerLink]="['/classes', c.slug]" [attr.aria-label]="c.title">
      <span class="class-card__mon">{{ on | when: 'mon' }}</span>
      <span class="class-card__day">{{ on | when: 'day' }}</span>
      <span class="class-card__dow">{{ on | when: 'dow' }}</span>
    </a>
    <div class="class-card__body">
      <p class="class-card__meta">
        <span>{{ c.type }}</span
        ><span>{{ c.level }}</span>
        @if (c.sessions > 1) {
          <span>{{ week() ? 'Week ' + week() + ' of ' + c.sessions : c.sessions + ' weeks' }}</span>
        }
      </p>
      <h3 class="class-card__title">
        <a [routerLink]="['/classes', c.slug]">{{ c.title }}</a>
      </h3>
      <p class="class-card__when">
        {{ c.cadence ? c.cadence + ' · ' : '' }}{{ c.start | when: 'time' : c.end }}
        @if (c.instructor) {
          <span class="class-card__by"
            >with
            <a class="linkish" [routerLink]="['/instructors', slug(c.instructor)]">{{
              c.instructor
            }}</a></span
          >
        }
      </p>
    </div>
    <div class="class-card__cta">
      <span class="class-card__price">{{ c.price | money }}</span>
      @if (c.spotsLeft != null && c.spotsLeft <= 4) {
        <span class="tag tag--hot">{{
          c.spotsLeft === 0 ? 'Sold out' : 'Only ' + c.spotsLeft + ' spots left'
        }}</span>
      }
      <a class="btn btn-dark btn-sm" [routerLink]="['/classes', c.slug]">{{
        c.price === 0 ? 'RSVP' : 'Reserve'
      }}</a>
    </div>
  `,
})
export class ClassCard {
  item = input.required<ArtClass>();
  /** When the card stands for one session of a weekly course: that session's date (YYYY-MM-DD) and week number. */
  date = input<string>();
  week = input<number>();
  /** Classes carry an instructor's name, not an id; the profile page is keyed on the slugified name. */
  protected slug = slugify;
}

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
  template: `
    @let e = item();
    <a class="class-card__date" routerLink="/exhibitions" [attr.aria-label]="e.title">
      <span class="class-card__mon">{{ date() | when: 'mon' }}</span>
      <span class="class-card__day">{{ date() | when: 'day' }}</span>
      <span class="class-card__dow">{{ date() | when: 'dow' }}</span>
    </a>
    <div class="class-card__body">
      <p class="class-card__meta">
        <span>Exhibition</span><span>{{ label() }}</span>
      </p>
      <h3 class="class-card__title">
        <a routerLink="/exhibitions">{{ e.title }}</a>
      </h3>
      <p class="class-card__when">
        @if (kind() === 'reception' && e.reception) {
          {{ e.reception.start | when: 'time' : e.reception.end }} ·
        }
        On view {{ e.start | when: 'range' : e.end }}
      </p>
    </div>
    <div class="class-card__cta">
      <span class="class-card__price">Free</span>
      <a class="btn btn-dark btn-sm" routerLink="/exhibitions">See the show</a>
    </div>
  `,
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

@Component({
  selector: 'app-product-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ArtImage, MoneyPipe],
  host: { class: 'product-card' },
  template: `
    @let p = item();
    <a [routerLink]="['/shop', p.slug]" class="product-card__link">
      <app-art-image [src]="p.image" [alt]="p.name" ratio="4 / 5" />
      @if (!p.inStock) {
        <span class="tag tag--sold">Sold</span>
      }
      <span class="product-card__name">{{ p.name }}</span>
    </a>
    <span class="product-card__foot">
      @if (p.artist) {
        <a [routerLink]="['/artists', p.artist.slug]">{{ p.artist.name }}</a>
      }
      <span class="product-card__price">{{ p.price | money }}</span>
    </span>
  `,
})
export class ProductCard {
  item = input.required<Product>();
}

@Component({
  selector: 'app-exhibition-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ArtImage, WhenPipe],
  host: { class: 'exhibit-card' },
  template: `
    @let e = item();
    <app-art-image [src]="e.image" [alt]="e.title" ratio="3 / 4" />
    <div class="exhibit-card__veil">
      <span class="tag" [class.tag--light]="status() === 'Now on view'">{{ status() }}</span>
      <h3>{{ e.title }}</h3>
      <p class="exhibit-card__dates">{{ e.start | when: 'range' : e.end }}</p>
      <p class="exhibit-card__sum">{{ e.summary }}</p>
    </div>
  `,
})
export class ExhibitionCard {
  item = input.required<Exhibition>();
  protected status = computed(() => exhibitionStatus(this.item()));
}

export function exhibitionStatus(e: Exhibition): 'Now on view' | 'Coming up' | 'Past' {
  const t = today();
  if (e.start > t) return 'Coming up';
  return e.end >= t ? 'Now on view' : 'Past';
}
