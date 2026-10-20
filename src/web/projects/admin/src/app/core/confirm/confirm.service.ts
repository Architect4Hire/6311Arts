import { Injectable, signal } from '@angular/core';

export interface ConfirmRequest {
  readonly heading: string;
  readonly message: string;
  readonly confirmLabel: string;
  readonly cancelLabel: string;
}

interface Pending extends ConfirmRequest {
  readonly settle: (confirmed: boolean) => void;
}

/**
 * Asks one yes/no question in a `ui-dialog` (rendered by `ConfirmHost`) and resolves with the answer. Closing the
 * dialog any other way — Escape, the close button — is "no", so a stray key never discards work.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly pending = signal<Pending | null>(null);

  readonly request = this.pending.asReadonly();

  confirm(request: ConfirmRequest): Promise<boolean> {
    // A second question while one is open answers the first "no": only one dialog can hold focus.
    this.pending()?.settle(false);

    return new Promise<boolean>((resolve) => {
      let settled = false;
      const pending: Pending = {
        ...request,
        settle: (confirmed) => {
          if (settled) return;
          settled = true;
          if (this.pending() === pending) this.pending.set(null);
          resolve(confirmed);
        },
      };
      this.pending.set(pending);
    });
  }

  answer(confirmed: boolean): void {
    this.pending()?.settle(confirmed);
  }
}
