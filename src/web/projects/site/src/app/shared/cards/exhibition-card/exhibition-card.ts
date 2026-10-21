import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Exhibition } from '../../../core/models';
import { ArtImage } from '../../media/art-image/art-image';
import { WhenPipe, today } from '../../format';

@Component({
  selector: 'app-exhibition-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ArtImage, WhenPipe],
  host: { class: 'exhibit-card' },
  templateUrl: './exhibition-card.html',
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
