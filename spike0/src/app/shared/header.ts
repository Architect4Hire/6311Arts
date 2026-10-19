import { ChangeDetectionStrategy, Component, afterNextRender, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { WpApi } from '../core/wp-api.service';
import { Icon } from './icon';

/** Fixed navbar: always charcoal; compacts once the page scrolls. */
@Component({
  selector: 'app-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, Icon],
  host: { class: 'nav', '[class.nav--solid]': 'solid() || open()', '[class.nav--open]': 'open()' },
  template: `
    <a class="skip" href="#content">Skip to content</a>
    <div class="nav__inner">
      <a class="nav__brand" routerLink="/" aria-label="6311arts home">
        <img class="mark mark--sm" src="/logo.webp" alt="" width="44" height="44" decoding="async" />
        <span class="nav__wordmark">6311arts</span>
      </a>
      <button class="nav__toggle" type="button" (click)="open.set(!open())" [attr.aria-expanded]="open()" aria-controls="nav-menu">
        <app-icon [name]="open() ? 'close' : 'menu'" [size]="26" />
        <span class="sr-only">Menu</span>
      </button>
      <nav id="nav-menu" class="nav__menu" aria-label="Main">
        <ul>
          @for (l of links; track l.path) {
            <li>
              <a [routerLink]="l.path" routerLinkActive="is-active" [routerLinkActiveOptions]="{ exact: l.path === '/' }">{{ l.label }}</a>
            </li>
          }
          <li>
            <a class="nav__cart" [href]="api.cartUrl()" aria-label="Cart"><app-icon name="cart" [size]="20" /></a>
          </li>
          <li><a class="btn btn-white btn-sm" routerLink="/memberships">Join</a></li>
        </ul>
      </nav>
    </div>
  `,
})
export class Header {
  protected api = inject(WpApi);
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
