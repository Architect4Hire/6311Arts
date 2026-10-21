import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Seo } from '../../core/seo.service';
import { SiteApi } from '../../services/site-api.service';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-artists',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, RouterLink, Reveal],
  templateUrl: './artists.html',
})
export class Artists {
  private all = toSignal(inject(SiteApi).artists(), { initialValue: [] });
  protected q = signal('');
  protected filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.all().filter((a) => !q || a.name.toLowerCase().includes(q));
  });
  protected letters = computed(() => {
    const map = new Map<string, ReturnType<typeof this.filtered>>();
    for (const a of this.filtered()) {
      const l = /[a-z]/i.test(a.name[0]) ? a.name[0].toUpperCase() : '#';
      map.set(l, [...(map.get(l) ?? []), a]);
    }
    return [...map]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([letter, items]) => ({ letter, items }));
  });

  constructor() {
    inject(Seo).set({
      title: 'Local Chicago Artists | North Side Painters, Printmakers & Makers',
      description:
        'Meet the 50+ artists who show and sell at 6311arts, all living within six miles of our Edgewater gallery. Browse their work and buy direct.',
      path: '/artists',
      breadcrumbs: [{ name: 'Artists', path: '/artists' }],
    });
  }
}
