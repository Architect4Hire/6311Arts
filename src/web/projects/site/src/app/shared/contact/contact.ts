import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiIcon } from '@6311/ui';
import { SITE } from '../../core/site';
import { FormsApi } from '../../services/forms-api.service';

/**
 * The template's contact section: visit details on the left, a "Say hello" form on the right, which sends to
 * `POST /api/v1/forms/contact` (ADR 0047): staff get the message, the sender a short confirmation.
 */
@Component({
  selector: 'app-contact',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, UiIcon],
  host: { class: 'band band--white contact' },
  templateUrl: './contact.html',
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
    if (!this.name || !/^\S+@\S+\.\S+$/.test(this.email) || !this.message)
      return this.state.set('error');
    this.state.set('sending');
    void this.forms
      .contact({ name: this.name, email: this.email, message: this.message, company: this.trap })
      .then((outcome) => this.state.set(outcome.ok ? 'done' : 'error'));
  }
}
