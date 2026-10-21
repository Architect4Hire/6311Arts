import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiIcon } from '@6311/ui';
import { SITE } from '../core/site';
import { FormsApi } from '../services/forms-api.service';

/**
 * The template's contact section: visit details on the left, a "Say hello" form on the right, which sends to
 * `POST /api/v1/forms/contact` (ADR 0047): staff get the message, the sender a short confirmation.
 */
@Component({
  selector: 'app-contact',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, UiIcon],
  host: { class: 'band band--white contact' },
  template: `
    <div class="container contact__grid">
      <div>
        <h2 class="band__title band__title--left">Visit us</h2>
        <p>
          We’re in a former funeral home in {{ site.address.neighborhood }}. Walk in during gallery hours,
          or call ahead on Monday and Tuesday and we’ll open up for you.
        </p>
        <p class="contact__big"><ui-icon name="phone" [size]="26" /> <a [href]="'tel:' + site.phone.replaceAll('-', '')">{{ site.phone }}</a></p>
        <p class="contact__line"><ui-icon name="mail" [size]="20" /> <a [href]="'mailto:' + site.email">{{ site.email }}</a></p>
        <p class="contact__line"><ui-icon name="pin" [size]="20" />
          <a [href]="site.mapUrl" target="_blank" rel="noopener">{{ site.address.street }}, {{ site.address.city }}, {{ site.address.region }} {{ site.address.postal }}</a>
        </p>
        <dl class="hours">
          @for (h of site.hours; track h.days) {
            <dt>{{ h.days }}</dt><dd>{{ h.time }}</dd>
          }
        </dl>
      </div>
      <div>
        <h2 class="band__title band__title--left">Say hello</h2>
        @if (state() === 'done') {
          <p class="subscribe__ok" role="status">Thanks, {{ name }}. We’ll be in touch soon.</p>
        } @else {
          <form class="form" (ngSubmit)="send()">
            <label><span class="sr-only">Your name</span>
              <input name="name" [(ngModel)]="name" required placeholder="Your name" autocomplete="name" /></label>
            <label><span class="sr-only">Your email</span>
              <input name="email" type="email" [(ngModel)]="email" required placeholder="Your email" autocomplete="email" /></label>
            <label><span class="sr-only">Message</span>
              <textarea name="message" rows="5" [(ngModel)]="message" required placeholder="Booking a visit, a private workshop, a question about a piece…"></textarea></label>
            <input class="hp" name="company" tabindex="-1" autocomplete="off" [(ngModel)]="trap" aria-hidden="true" />
            <button class="btn btn-dark" type="submit" [disabled]="state() === 'sending'">{{ state() === 'sending' ? 'Sending…' : 'Send' }}</button>
            @if (state() === 'error') {
              <p class="subscribe__err" role="alert">Please fill in every field, or email us at {{ site.email }}.</p>
            }
          </form>
        }
      </div>
    </div>
  `,
})
export class Contact {
  protected site = SITE;
  private forms = inject(FormsApi);
  protected name = '';
  protected email = '';
  protected message = '';
  protected trap = '';
  protected state = signal<'idle' | 'sending' | 'done' | 'error'>('idle');

  send(): void {
    if (!this.name || !/^\S+@\S+\.\S+$/.test(this.email) || !this.message) return this.state.set('error');
    this.state.set('sending');
    void this.forms
      .contact({ name: this.name, email: this.email, message: this.message, company: this.trap })
      .then((outcome) => this.state.set(outcome.ok ? 'done' : 'error'));
  }
}
