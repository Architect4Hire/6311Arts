import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../../../core/models';
import { ArtImage } from '../../media/art-image/art-image';
import { MoneyPipe } from '../../format';

@Component({
  selector: 'app-product-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ArtImage, MoneyPipe],
  host: { class: 'product-card' },
  templateUrl: './product-card.html',
})
export class ProductCard {
  item = input.required<Product>();
}
