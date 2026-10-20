import { DOCUMENT } from '@angular/common';
import { DestroyRef, afterNextRender, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from '../confirm/confirm.service';

/** An editor screen that can hold work nobody has saved yet (frontend.md, Admin). */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Leaving a screen with unsaved edits asks first. Route with `canDeactivate: [unsavedChangesGuard]`, and call
 * `warnBeforeUnload` from the same component to cover closing the tab or reloading, which the router never sees.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!component?.hasUnsavedChanges()) return true;

  return inject(ConfirmService).confirm({
    heading: 'Discard unsaved changes?',
    message: 'You have edits on this screen that haven’t been saved. Leaving now loses them.',
    confirmLabel: 'Discard changes',
    cancelLabel: 'Keep editing',
  });
};

/**
 * The browser's own "Leave site?" prompt while `hasUnsavedChanges` is true. Call from a component constructor;
 * the listener goes when the component does.
 */
export function warnBeforeUnload(hasUnsavedChanges: () => boolean): void {
  const window = inject(DOCUMENT).defaultView;
  const destroyRef = inject(DestroyRef);

  afterNextRender(() => {
    if (!window) return;
    const listener = (event: BeforeUnloadEvent) => {
      if (hasUnsavedChanges()) event.preventDefault();
    };
    window.addEventListener('beforeunload', listener);
    destroyRef.onDestroy(() => window.removeEventListener('beforeunload', listener));
  });
}
