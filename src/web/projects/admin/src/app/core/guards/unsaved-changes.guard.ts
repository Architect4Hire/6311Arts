import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, afterNextRender, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from '../confirm/confirm.service';

/** An editor screen that can hold work nobody has saved yet (frontend.md, Admin). */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
  /** What is at stake, when it is not edits — uploads still running, say. Defaults to unsaved edits. */
  unsavedChangesMessage?(): string;
}

/** What every "discard and leave?" question says when the screen has nothing more specific. */
export const UNSAVED_EDITS_MESSAGE =
  'You have edits on this screen that haven’t been saved. Leaving now loses them.';

/**
 * The one place the CMS decides whether unsaved work may be thrown away, for every way out of a screen: a router
 * navigation (`unsavedChangesGuard`), signing out (the shell), and closing or reloading the tab
 * (`warnBeforeUnload`). Each asks the same question in the same dialog, and a "discard" given for leaving the app
 * is final: the browser's own "Leave site?" prompt does not ask a second time.
 */
@Injectable({ providedIn: 'root' })
export class UnsavedChanges {
  private readonly confirm = inject(ConfirmService);
  private readonly screens = new Set<() => boolean>();
  /** Set once the person has chosen to discard their work and leave the app. */
  private released = false;

  /** Whether any open screen holds unsaved work. */
  any(): boolean {
    for (const hasUnsavedChanges of this.screens) if (hasUnsavedChanges()) return true;
    return false;
  }

  /** Counts a screen's unsaved work in {@link any} until the returned function is called. */
  watch(hasUnsavedChanges: () => boolean): () => void {
    this.screens.add(hasUnsavedChanges);
    return () => this.screens.delete(hasUnsavedChanges);
  }

  /** Asks whether to throw the unsaved work away. */
  confirmDiscard(message: string = UNSAVED_EDITS_MESSAGE): Promise<boolean> {
    return this.confirm.confirm({
      heading: 'Discard unsaved changes?',
      message,
      confirmLabel: 'Discard changes',
      cancelLabel: 'Keep editing',
    });
  }

  /**
   * Before leaving the app for another page (signing out): asks only when something is unsaved, and a yes
   * {@link release}s the browser's prompt.
   */
  async confirmLeavingApp(): Promise<boolean> {
    if (!this.any()) return true;
    const discard = await this.confirmDiscard();
    if (discard) this.release();
    return discard;
  }

  /** The person has agreed to lose their work: the browser's prompt stays quiet from here on. */
  release(): void {
    this.released = true;
  }

  /** The leave did not happen (signing out failed), so unsaved work is worth protecting again. */
  hold(): void {
    this.released = false;
  }

  /** Whether closing or reloading the tab should still make the browser ask. */
  shouldWarnBeforeUnload(hasUnsavedChanges: () => boolean): boolean {
    return !this.released && hasUnsavedChanges();
  }
}

/**
 * Leaving a screen with unsaved edits asks first. Route with `canDeactivate: [unsavedChangesGuard]`, and call
 * `warnBeforeUnload` from the same component to cover closing the tab or reloading, which the router never sees.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!component?.hasUnsavedChanges()) return true;

  return inject(UnsavedChanges).confirmDiscard(component.unsavedChangesMessage?.());
};

/**
 * The browser's own "Leave site?" prompt while `hasUnsavedChanges` is true, unless the person has already chosen
 * to discard and leave (`UnsavedChanges.release`). Also counts the screen in `UnsavedChanges.any`, so signing out
 * asks about it. Call from a component constructor; both go when the component does.
 */
export function warnBeforeUnload(hasUnsavedChanges: () => boolean): void {
  const window = inject(DOCUMENT).defaultView;
  const destroyRef = inject(DestroyRef);
  const unsaved = inject(UnsavedChanges);

  destroyRef.onDestroy(unsaved.watch(hasUnsavedChanges));

  afterNextRender(() => {
    if (!window) return;
    const listener = (event: BeforeUnloadEvent) => {
      if (unsaved.shouldWarnBeforeUnload(hasUnsavedChanges)) event.preventDefault();
    };
    window.addEventListener('beforeunload', listener);
    destroyRef.onDestroy(() => window.removeEventListener('beforeunload', listener));
  });
}
