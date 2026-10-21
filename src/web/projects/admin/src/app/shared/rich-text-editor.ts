import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { UiButton, UiIcon } from '@6311/ui';

let nextId = 0;

/** What the toolbar shows as on, for the selection as it stands. */
interface Active {
  readonly paragraph: boolean;
  readonly heading: boolean;
  readonly bold: boolean;
  readonly italic: boolean;
  readonly bulletList: boolean;
  readonly orderedList: boolean;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
}

const NOTHING_ACTIVE: Active = {
  paragraph: false,
  heading: false,
  bold: false,
  italic: false,
  bulletList: false,
  orderedList: false,
  canUndo: false,
  canRedo: false,
};

/**
 * The CMS's rich-text body (`<cms-rich-text [(value)] label>`), on Tiptap 3 (ADR 0034, 0045). Its schema is the
 * server's allow-list and nothing more: paragraphs, one heading level, bold, italic, lists and line breaks. So what
 * staff can make is what the API keeps, and it is the same everywhere the editor is used.
 *
 * - **No links or pictures** at this time (ADR 0045): a record's pictures are its hero or gallery. A pasted link
 *   keeps its words and a pasted picture is dropped, as the server would drop them.
 * - `value` is HTML. Loading a body does not emit, so an untouched body never looks edited; every edit emits the
 *   editor's HTML (`''` when empty).
 *
 * The toolbar is a labelled group of ordinary buttons, with `aria-pressed` for what applies to the selection. The
 * text area is a labelled, described `textbox`. Buttons keep focus in the text when clicked (`mousedown` default
 * prevented), so the selection they act on is not lost.
 */
@Component({
  selector: 'cms-rich-text',
  imports: [UiButton, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="field">
      <span class="field__label" [id]="labelId">{{ label() }}</span>

      <!--
        One frame around the toolbar and the text, so they read as one control. Focus shows once, on the frame,
        while the text has it; the buttons keep their own focus ring.
      -->
      <div
        class="editor"
        [class.editor--focused]="focused()"
        [class.editor--invalid]="!!error()"
        [class.editor--disabled]="disabled()"
      >
        <!--
        A plain group, not role="toolbar": that role tells a screen-reader user the buttons are one tab stop
        navigated with the arrow keys, and these are individually tab-reachable. Claiming the role without a
        roving tabindex is a worse lie than not claiming it.
      -->
        <div
          class="toolbar"
          role="group"
          [attr.aria-label]="label() + ' formatting'"
          [attr.aria-controls]="surfaceId"
        >
          <span class="group">
            @for (tool of blocks; track tool.key) {
              <button
                uiButton
                type="button"
                size="sm"
                [variant]="active()[tool.key] ? 'dark' : 'border-dark'"
                [attr.aria-pressed]="active()[tool.key]"
                [disabled]="disabled()"
                (mousedown)="$event.preventDefault()"
                (click)="block(tool.key)"
              >
                {{ tool.label }}
              </button>
            }
          </span>
          <span class="group">
            <button
              uiButton
              type="button"
              size="sm"
              [variant]="active().bold ? 'dark' : 'border-dark'"
              [attr.aria-pressed]="active().bold"
              [disabled]="disabled()"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.toggleBold())"
            >
              Bold
            </button>
            <button
              uiButton
              type="button"
              size="sm"
              [variant]="active().italic ? 'dark' : 'border-dark'"
              [attr.aria-pressed]="active().italic"
              [disabled]="disabled()"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.toggleItalic())"
            >
              Italic
            </button>
          </span>
          <span class="group">
            <button
              uiButton
              type="button"
              size="sm"
              [variant]="active().bulletList ? 'dark' : 'border-dark'"
              [attr.aria-pressed]="active().bulletList"
              [disabled]="disabled()"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.toggleBulletList())"
            >
              Bullets
            </button>
            <button
              uiButton
              type="button"
              size="sm"
              [variant]="active().orderedList ? 'dark' : 'border-dark'"
              [attr.aria-pressed]="active().orderedList"
              [disabled]="disabled()"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.toggleOrderedList())"
            >
              Numbers
            </button>
          </span>
          <span class="group">
            <button
              uiButton
              type="button"
              size="sm"
              variant="border-dark"
              [disabled]="disabled() || !active().canUndo"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.undo())"
            >
              Undo
            </button>
            <button
              uiButton
              type="button"
              size="sm"
              variant="border-dark"
              [disabled]="disabled() || !active().canRedo"
              (mousedown)="$event.preventDefault()"
              (click)="run((chain) => chain.redo())"
            >
              Redo
            </button>
          </span>
        </div>

        <div #surface class="surface"></div>
      </div>

      @if (hint()) {
        <span class="field__hint" [id]="hintId">{{ hint() }}</span>
      }
      @if (error()) {
        <span class="field__error" [id]="errorId"
          ><ui-icon name="stop" [size]="16" [stroke]="2.4" />{{ error() }}</span
        >
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .editor {
      border: var(--border) solid var(--line);
      border-radius: var(--radius);
      background: var(--white);
      transition: border-color var(--dur-fast);
    }
    .editor--focused {
      border-color: var(--ink);
    }
    /* An invalid body keeps its danger border while focused, as every other control does. */
    .editor--invalid {
      border-color: var(--danger);
    }
    .editor--disabled {
      background: var(--light);
      color: var(--muted);
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-3);
      padding: var(--space-2);
      border-bottom: var(--border-thin) solid var(--line);
      background: var(--light);
    }
    .group {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    .surface {
      overflow-wrap: anywhere;
    }
    /*
      Tiptap makes the editable element itself, so only ::ng-deep reaches it. It fills the frame, so a click
      anywhere below the text lands in it, and it draws no outline of its own: the frame shows its focus, and the
      global :focus-visible ring would otherwise draw a second box inside the first.
    */
    .surface ::ng-deep .ProseMirror {
      min-height: 16rem;
      padding: var(--control-pad);
      outline: none;
    }
    .surface ::ng-deep .ProseMirror > :first-child {
      margin-top: 0;
    }
    .surface ::ng-deep .ProseMirror > :last-child {
      margin-bottom: 0;
    }
  `,
})
export class RichTextEditor {
  /** The body as HTML. Two-way: edits are written back; a new value from outside replaces the text. */
  readonly value = model.required<string>();
  readonly label = input.required<string>();
  readonly hint = input<string>();
  /** A message for the body as a whole, from the form or the API. Marks the text invalid and describes it. */
  readonly error = input<string | null>(null);
  readonly disabled = input(false);

  private readonly surface = viewChild.required<ElementRef<HTMLElement>>('surface');

  private readonly id = `cms-rich-text-${nextId++}`;
  protected readonly labelId = `${this.id}-label`;
  protected readonly hintId = `${this.id}-hint`;
  protected readonly errorId = `${this.id}-error`;
  protected readonly surfaceId = `${this.id}-surface`;

  protected readonly blocks = [
    { key: 'paragraph', label: 'Text' },
    { key: 'heading', label: 'Heading' },
  ] as const;

  /** The Tiptap editor, once it exists (after the first render). */
  private editor: Editor | null = null;
  /** The HTML the editor holds, so a value written back by this component is not loaded again. */
  private held: string | null = null;

  protected readonly active = signal<Active>(NOTHING_ACTIVE);
  /** Whether the text has focus, so the frame can show it. */
  protected readonly focused = signal(false);

  private readonly describedBy = computed(
    () =>
      [this.hint() ? this.hintId : null, this.error() ? this.errorId : null]
        .filter(Boolean)
        .join(' ') || null,
  );

  constructor() {
    afterNextRender(() => this.create());
    inject(DestroyRef).onDestroy(() => this.editor?.destroy());

    // A value from outside (a load, a discard, the saved version after a conflict) replaces the text, without emitting.
    effect(() => {
      const value = this.value();
      untracked(() => {
        if (this.editor && value !== this.held) {
          this.held = value;
          this.editor.commands.setContent(value, { emitUpdate: false });
          this.refresh();
        }
      });
    });

    effect(() => {
      const disabled = this.disabled();
      untracked(() => this.editor?.setEditable(!disabled, false));
    });

    // The text area's accessible name and description follow the label, hint and error.
    effect(() => {
      const describedBy = this.describedBy();
      const invalid = this.error() ? 'true' : null;
      untracked(() => {
        const element = this.editor?.view.dom;
        if (!element) return;
        setOrRemove(element, 'aria-describedby', describedBy);
        setOrRemove(element, 'aria-invalid', invalid);
      });
    });
  }

  /** Focuses the text: the form sends focus here when the body has an error. */
  focus(): void {
    this.editor?.commands.focus();
  }

  protected run(
    command: (chain: ReturnType<Editor['chain']>) => ReturnType<Editor['chain']>,
  ): void {
    if (!this.editor || this.disabled()) return;
    command(this.editor.chain().focus()).run();
  }

  protected block(key: (typeof this.blocks)[number]['key']): void {
    this.run((chain) =>
      key === 'paragraph' ? chain.setParagraph() : chain.toggleHeading({ level: 2 }),
    );
  }

  private create(): void {
    const value = this.value();
    this.held = value;
    this.editor = new Editor({
      element: this.surface().nativeElement,
      editable: !this.disabled(),
      content: value,
      extensions: [
        // No image extension and no link mark: a pasted picture is dropped and a pasted link keeps its words.
        StarterKit.configure({
          heading: { levels: [2] },
          blockquote: false,
          code: false,
          codeBlock: false,
          horizontalRule: false,
          link: false,
          strike: false,
          underline: false,
        }),
      ],
      editorProps: {
        attributes: {
          id: this.surfaceId,
          role: 'textbox',
          'aria-multiline': 'true',
          'aria-labelledby': this.labelId,
          ...(this.describedBy() ? { 'aria-describedby': this.describedBy()! } : {}),
        },
      },
      onUpdate: ({ editor }) => {
        const html = editor.isEmpty ? '' : editor.getHTML();
        this.held = html;
        this.value.set(html);
        this.refresh();
      },
      onSelectionUpdate: () => this.refresh(),
      onTransaction: () => this.refresh(),
      onFocus: () => this.focused.set(true),
      onBlur: () => this.focused.set(false),
    });
    this.refresh();
  }

  private refresh(): void {
    const editor = this.editor;
    if (!editor) return;
    this.active.set({
      paragraph: editor.isActive('paragraph'),
      heading: editor.isActive('heading', { level: 2 }),
      bold: editor.isActive('bold'),
      italic: editor.isActive('italic'),
      bulletList: editor.isActive('bulletList'),
      orderedList: editor.isActive('orderedList'),
      canUndo: editor.can().undo(),
      canRedo: editor.can().redo(),
    });
  }
}

function setOrRemove(element: Element, name: string, value: string | null): void {
  if (value === null) element.removeAttribute(name);
  else element.setAttribute(name, value);
}
