import { ChangeDetectionStrategy, Component, afterNextRender, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { LegacyStore } from '../../../core/legacy-store';

/** Fixed navbar: always charcoal; compacts once the page scrolls. */
@Component({
  selector: 'app-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, UiIcon],
  host: { class: 'nav', '[class.nav--solid]': 'solid() || open()', '[class.nav--open]': 'open()' },
  templateUrl: './header.html',
})
export class Header {
  protected store = inject(LegacyStore);
  protected solid = signal(false);
  protected open = signal(false);

  protected links = [
    { path: '/', label: 'Home' },
    { path: '/classes', label: 'Classes' },
    { path: '/exhibitions', label: 'Exhibitions' },
    { path: '/artists', label: 'Artists' },
    { path: '/shop', label: 'Shop' },
    { path: '/about', label: 'About' },
    { path: '/visit', label: 'Visit' },
  ];

  constructor() {
    inject(Router)
      .events.pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.open.set(false));
    afterNextRender(() => {
      const onScroll = () => this.solid.set(window.scrollY > 40);
      onScroll();
      window.addEventListener('scroll', onScroll, { passive: true });
    });
  }
}
