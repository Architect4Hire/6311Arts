import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHost } from './core/confirm/confirm-host';
import { ToastHost } from './core/toast/toast-host';

/** The root: the routed screen, plus the two surfaces every screen shares — toasts and the confirm dialog. */
@Component({
  selector: 'cms-root',
  imports: [RouterOutlet, ToastHost, ConfirmHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <cms-toast-host />
    <cms-confirm-host />
  `,
})
export class App {}
