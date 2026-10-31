import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiButton } from '@6311/ui';
import { EditorView } from '../record-editor';

/**
 * A record editor's Save (or Create) and Discard buttons. Placed inside the editor's `<form>`, so Save submits it.
 *
 * ```html
 * <cms-save-actions [view]="view" />
 * ```
 */
@Component({
  selector: 'cms-save-actions',
  imports: [UiButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './save-actions.html',
  styleUrl: './save-actions.scss',
})
export class SaveActions {
  readonly view = input.required<EditorView>();
}
