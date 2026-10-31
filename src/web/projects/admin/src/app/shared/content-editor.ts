import { Directive } from '@angular/core';
import { STATUS_CHIPS } from '../models/content';
import {
  ContentLifecycle,
  ContentLifecycleApi,
  LifecycleCopy,
  LifecycleRecord,
} from './content-lifecycle';
import { EditorChip, RecordEditor, RecordEditorApi, RecordEditorConfig } from './record-editor';

/** A publishable type's editor config: the record editor's, plus how its lifecycle is talked about. */
export interface ContentEditorConfig<
  T extends LifecycleRecord,
  D,
  C,
  U extends object,
  F extends string,
> extends RecordEditorConfig<T, D, C, U, F> {
  readonly api: RecordEditorApi<T, C, U> & ContentLifecycleApi<T>;
  readonly lifecycle: LifecycleCopy;
  /** Router state the list reads after a delete, if it says something about it. */
  readonly deletedState?: (saved: T) => Record<string, unknown>;
}

/**
 * The editor every publishable type shares: a {@link RecordEditor} with the publishing lifecycle (content.md) that
 * `<cms-publishing-panel>` shows. Status commands act on the saved version, so they wait until the form is saved.
 */
@Directive()
export abstract class ContentEditor<
  T extends LifecycleRecord,
  D,
  C,
  U extends object,
  F extends string,
> extends RecordEditor<T, D, C, U, F> {
  /** Publish, unpublish, schedule, archive and delete. */
  protected readonly lifecycle: ContentLifecycle<T>;

  constructor(config: ContentEditorConfig<T, D, C, U, F>) {
    super(config);
    this.lifecycle = new ContentLifecycle({
      copy: config.lifecycle,
      api: config.api,
      record: () => this.record(),
      label: config.label,
      dirty: () => this.dirty(),
      busy: () => this.busy(),
      replace: (saved) => this.show(saved),
      clearErrors: () => {
        this.errors.set({});
        this.formError.set(null);
      },
      refused: (message, problem) => this.showErrors(this.fieldErrorsOf(problem), message),
      announce: (message) => this.announce(message),
      deleted: (saved) => {
        // Back to the saved version first, so the unsaved-changes guard has nothing to ask about on the way out.
        this.draft.set(config.draftOf(saved));
        return this.router.navigate(
          [config.route],
          config.deletedState ? { state: config.deletedState(saved) } : {},
        );
      },
    });
  }

  protected override pending(): boolean {
    return this.lifecycle.busy();
  }

  protected override chipOf(saved: T): EditorChip {
    return STATUS_CHIPS[saved.status];
  }
}
