import { Injectable, signal } from '@angular/core';
import { NoticeTone } from '@6311/ui';

export interface Toast {
  readonly id: number;
  readonly tone: NoticeTone;
  readonly heading: string;
  readonly message?: string;
  /** The correlation id, shown so a support request and the dashboard's trace search use the same value. */
  readonly reference?: string;
}

/** Enough to see what just happened without burying the screen; the oldest give way. */
const MAX_VISIBLE = 4;

/**
 * The CMS's one place for messages that are not tied to a field: failed requests, mostly. Toasts stay until
 * dismissed — an error that disappears on a timer is gone before some people have read it (WCAG 2.2.1).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly items = signal<readonly Toast[]>([]);

  readonly toasts = this.items.asReadonly();

  show(toast: Omit<Toast, 'id'>): number {
    const id = this.nextId++;
    this.items.update((items) => {
      // The same failure twice in a row (a double click, a retry) is one message, not a stack of them.
      const rest = items.filter(
        (item) => !(item.heading === toast.heading && item.message === toast.message),
      );
      return [...rest, { ...toast, id }].slice(-MAX_VISIBLE);
    });
    return id;
  }

  dismiss(id: number): void {
    this.items.update((items) => items.filter((item) => item.id !== id));
  }
}
