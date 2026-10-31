import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiButton, UiNotice, UiStatusChip } from '@6311/ui';
import { EditorView } from '../record-editor';

/**
 * Everything around a record editor's form, the same for every type: the way back to the list, the live region,
 * loading / failed / not-found, the title with its status chip, the 409 comparison and the form-level error. The
 * form itself is projected, and only rendered once the record has loaded.
 *
 * ```html
 * <cms-editor-frame [view]="view">
 *   @if (loaded()) { <div class="layout">…</div> }
 * </cms-editor-frame>
 * ```
 */
@Component({
  selector: 'cms-editor-frame',
  imports: [RouterLink, UiButton, UiNotice, UiStatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './editor-frame.html',
  styleUrl: './editor-frame.scss',
})
export class EditorFrame {
  readonly view = input.required<EditorView>();
}
