import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FormsApi } from '../services/forms-api.service';

/** The template's "Get notified!" band: a sign-up for the newsletter list (`POST /api/v1/forms/newsletter`, ADR 0047). */
@Component({
  selector: 'app-newsletter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  host: { class: 'band band--dark text-center' },
  template: `
    <div class="container container--narrow">
      <h2 class="band__title">{{ heading() }}</h2>
      <p class="band__lead">{{ lead() }}</p>
      @if (state() === 'done') {
        <p class="subscribe__ok" role="status">You’re on the list. See you at the next opening.</p>
      } @else {
        <form class="subscribe" (ngSubmit)="submit()" #f="ngForm">
          <label class="sr-only" for="nl-email">Email address</label>
          <input id="nl-email" name="email" type="email" required email [(ngModel)]="email" placeholder="Email address…" autocomplete="email" />
          <!-- Spam trap: people never see or fill this. -->
          <input class="hp" name="company" tabindex="-1" autocomplete="off" [(ngModel)]="trap" aria-hidden="true" />
          <button class="btn btn-white" type="submit" [disabled]="state() === 'sending'">
            {{ state() === 'sending' ? 'Sending…' : 'Get notified!' }}
          </button>
        </form>
        @if (state() === 'error') {
          <p class="subscribe__err" role="alert">That didn’t go through. Check the address and try again.</p>
        }
      }
    </div>
  `,
})
export class Newsletter {
  heading = input('Get notified!');
  lead = input('Openings, new classes and artist calls, about twice a month. No spam, ever.');

  private forms = inject(FormsApi);
  protected email = '';
  protected trap = '';
  protected state = signal<'idle' | 'sending' | 'done' | 'error'>('idle');

  submit(): void {
    if (!/^\S+@\S+\.\S+$/.test(this.email)) return this.state.set('error');
    this.state.set('sending');
    void this.forms.subscribe(this.email, this.trap).then((outcome) => this.state.set(outcome.ok ? 'done' : 'error'));
  }
}
