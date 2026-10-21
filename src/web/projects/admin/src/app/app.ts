import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHost } from './core/confirm/confirm-host/confirm-host';
import { ToastHost } from './core/toast/toast-host/toast-host';

/** The root: the routed screen, plus the two surfaces every screen shares — toasts and the confirm dialog. */
@Component({
  selector: 'cms-root',
  imports: [RouterOutlet, ToastHost, ConfirmHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
})
export class App {}
