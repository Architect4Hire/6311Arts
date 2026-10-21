import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FormsApi } from '../../../services/forms-api.service';

/** The template's "Get notified!" band: a sign-up for the newsletter list (`POST /api/v1/forms/newsletter`, ADR 0047). */
@Component({
  selector: 'app-newsletter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  host: { class: 'band band--dark text-center' },
  templateUrl: './newsletter.html',
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
    void this.forms
      .subscribe(this.email, this.trap)
      .then((outcome) => this.state.set(outcome.ok ? 'done' : 'error'));
  }
}
