import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SITE } from '../core/site';
import { Icon } from './icon';

@Component({
  selector: 'app-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  host: { class: 'footer' },
  template: `
    <div class="footer__grid container">
      <div>
        <span class="mark" aria-hidden="true"><span>6311</span><span>arts</span></span>
        <p class="footer__blurb">An art center that makes room for everyone. Gallery, shop and classes on Clark Street since {{ site.founded }}.</p>
      </div>
      <div>
        <h3 class="footer__h">Visit</h3>
        <p>{{ site.address.street }}<br />{{ site.address.city }}, {{ site.address.region }} {{ site.address.postal }}</p>
        <p><a [href]="'tel:' + site.phone.replaceAll('-', '')">{{ site.phone }}</a><br /><a [href]="'mailto:' + site.email">{{ site.email }}</a></p>
      </div>
      <div>
        <h3 class="footer__h">Hours</h3>
        <dl class="footer__hours">
          @for (h of site.hours; track h.days) {
            <dt>{{ h.days }}</dt><dd>{{ h.time }}</dd>
          }
        </dl>
      </div>
      <div>
        <h3 class="footer__h">Explore</h3>
        <ul class="footer__links">
          <li><a routerLink="/classes">Classes & workshops</a></li>
          <li><a routerLink="/exhibitions">Exhibitions</a></li>
          <li><a routerLink="/shop">Shop local art</a></li>
          <li><a routerLink="/memberships">Memberships</a></li>
          <li><a routerLink="/submit">Submit your work</a></li>
        </ul>
      </div>
    </div>
    <div class="footer__bar container">
      <span class="footer__star"><app-icon name="star" [size]="20" [stroke]="2" /></span>
      <span>© {{ year }} {{ site.name }} · <a routerLink="/privacy">Privacy</a> · <a routerLink="/terms">Terms</a></span>
      <span class="footer__social">
        <a [href]="site.instagram" target="_blank" rel="noopener" aria-label="Instagram"><app-icon name="instagram" [size]="26" /></a>
        <a [href]="'mailto:' + site.email" aria-label="Email"><app-icon name="mail" [size]="26" /></a>
      </span>
    </div>
  `,
})
export class Footer {
  protected site = SITE;
  protected year = new Date().getFullYear();
}
