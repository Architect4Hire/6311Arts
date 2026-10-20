import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
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
import Image from '@tiptap/extension-image';
import StarterKit from '@tiptap/starter-kit';
import { UiButton, UiControl, UiField, UiIcon } from '@6311/ui';
import { MediaPicker } from '../features/media/media-picker';
import { MediaAssetSummary } from '../models/media';
import { isAllowedLink, isMediaImageSrc, mediaImageSrc } from '../models/html';

let nextId = 0;

/** What the toolbar shows as on, for the selection as it stands. */
interface Active {
  readonly paragraph: boolean;
  readonly h2: boolean;
  readonly h3: boolean;
  readonly h4: boolean;
  readonly bold: boolean;
  readonly italic: boolean;
  readonly bulletList: boolean;
  readonly orderedList: boolean;
  readonly blockquote: boolean;
  readonly link: boolean;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
}

const NOTHING_ACTIVE: Active = {
  paragraph: false,
  h2: false,
  h3: false,
  h4: false,
  bold: false,
  italic: false,
  bulletList: false,
  orderedList: false,
  blockquote: false,
  link: false,
  canUndo: false,
  canRedo: false,
};

/** A picture chosen from the library, waiting for its alt text before it goes in. */
interface PendingImage {
  readonly assetId: string;
  readonly thumbnailUrl: string | null;
}

/**
 * The CMS's rich-text body (`<cms-rich-text [(value)] label>`), on Tiptap 3 (ADR 0034). Its schema is the server's
 * allow-list and nothing more: paragraphs, headings 2–4, bold, italic, lists, quotes, line breaks, links and library
 * pictures. So what staff can make is what the API keeps.
 *
 * - **Links** take an address from this site (`/visit`), the page (`#hours`), the web, email or phone, checked as the
 *   server checks them; no "open in a new tab".
 * - **Pictures** come only from the media library and go in only with alt text: Media's, if it has one, which the
 *   person can change.
 * - `value` is HTML. Loading a body does not emit, so an untouched body never looks edited; every edit emits the
 *   editor's HTML (`''` when empty).
 *
 * The toolbar is a labelled `toolbar` of ordinary buttons, with `aria-pressed` for what applies to the selection. The
 * text area is a labelled, described `textbox`. Buttons keep focus in the text when clicked (`mousedown` default
 * prevented), so the selection they act on is not lost.
 */
@Component({
  selector: 'cms-rich-text',
  imports: [MediaPicker, UiButton, UiControl, UiField, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="field">
      <span class="field__label" [id]="labelId">{{ label() }}</span>

      <div
        class="toolbar"
        role="toolbar"
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
          <button
            uiButton
            type="button"
            size="sm"
            [variant]="active().blockquote ? 'dark' : 'border-dark'"
            [attr.aria-pressed]="active().blockquote"
            [disabled]="disabled()"
            (mousedown)="$event.preventDefault()"
            (click)="run((chain) => chain.toggleBlockquote())"
          >
            Quote
          </button>
        </span>
        <span class="group">
          <button
            uiButton
            type="button"
            size="sm"
            [variant]="active().link ? 'dark' : 'border-dark'"
            [attr.aria-pressed]="active().link"
            [attr.aria-expanded]="panel() === 'link'"
            [disabled]="disabled()"
            (mousedown)="$event.preventDefault()"
            (click)="openLink()"
          >
            Link
          </button>
          <button
            uiButton
            type="button"
            size="sm"
            variant="border-dark"
            [disabled]="disabled()"
            (click)="picking.set(true)"
          >
            Picture
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

      @if (panel() === 'link') {
        <div class="panel" role="group" aria-label="Link">
          <ui-field
            label="Link address"
            hint="A page on this site (/visit), a place on this page (#hours), or a web, email (mailto:) or phone (tel:) address."
            [error]="linkError()"
          >
            <input
              #linkInput
              uiControl
              name="linkHref"
              autocomplete="off"
              spellcheck="false"
              [value]="linkHref()"
              (input)="linkHref.set(linkInput.value)"
              (keydown.enter)="$event.preventDefault(); applyLink()"
              (keydown.escape)="closePanel()"
            />
          </ui-field>
          <div class="actions">
            <button uiButton type="button" size="sm" variant="dark" (click)="applyLink()">
              Apply link
            </button>
            @if (active().link) {
              <button uiButton type="button" size="sm" (click)="removeLink()">Remove link</button>
            }
            <button uiButton type="button" size="sm" (click)="closePanel()">Cancel</button>
          </div>
        </div>
      }

      @if (pendingImage(); as pending) {
        <div class="panel" role="group" aria-label="Picture">
          @if (pending.thumbnailUrl) {
            <img class="panel__thumb" [src]="pending.thumbnailUrl" alt="" />
          }
          <ui-field
            label="Alt text"
            hint="Describe the picture for people who can't see it. The site shows this in its place."
            [required]="true"
            [error]="altError()"
          >
            <input
              #altInput
              uiControl
              name="imageAlt"
              [value]="altText()"
              (input)="altText.set(altInput.value)"
              (keydown.enter)="$event.preventDefault(); insertImage()"
              (keydown.escape)="closePanel()"
            />
          </ui-field>
          <div class="actions">
            <button uiButton type="button" size="sm" variant="dark" (click)="insertImage()">
              Insert picture
            </button>
            <button uiButton type="button" size="sm" (click)="closePanel()">Cancel</button>
          </div>
        </div>
      }

      <div
        #surface
        class="surface field__control"
        [class.surface--disabled]="disabled()"
        [attr.aria-invalid]="error() ? 'true' : null"
      ></div>

      @if (hint()) {
        <span class="field__hint" [id]="hintId">{{ hint() }}</span>
      }
      @if (error()) {
        <span class="field__error" [id]="errorId"
          ><ui-icon name="stop" [size]="16" [stroke]="2.4" />{{ error() }}</span
        >
      }
    </div>

    <cms-media-picker [(open)]="picking" (picked)="picked($event)" />
  `,
  styles: `
    :host {
      display: block;
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-3);
      margin-bottom: var(--space-2);
    }
    .group {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    .panel {
      display: grid;
      gap: var(--space-3);
      margin-bottom: var(--space-2);
      padding: var(--space-3);
      border: var(--border-thin) solid var(--line);
      background: var(--light);
    }
    .panel__thumb {
      max-width: 160px;
      height: auto;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .surface {
      min-height: 16rem;
      padding: var(--space-3);
      overflow-wrap: anywhere;
    }
    .surface--disabled {
      opacity: 0.7;
    }
    .surface :focus {
      outline: none;
    }
    .surface:focus-within {
      outline: 2px solid var(--focus, var(--ink));
      outline-offset: 2px;
    }
    .surface ::ng-deep img {
      max-width: 100%;
      height: auto;
    }
    .surface ::ng-deep img.ProseMirror-selectednode {
      outline: 3px solid var(--ink);
    }
    .surface ::ng-deep blockquote {
      margin-left: 0;
      padding-left: var(--space-3);
      border-left: 4px solid var(--line);
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
  private readonly linkInput = viewChild<ElementRef<HTMLInputElement>>('linkInput');
  private readonly altInput = viewChild<ElementRef<HTMLInputElement>>('altInput');
  private readonly injector = inject(Injector);

  private readonly id = `cms-rich-text-${nextId++}`;
  protected readonly labelId = `${this.id}-label`;
  protected readonly hintId = `${this.id}-hint`;
  protected readonly errorId = `${this.id}-error`;
  protected readonly surfaceId = `${this.id}-surface`;

  protected readonly blocks = [
    { key: 'paragraph', label: 'Text' },
    { key: 'h2', label: 'Heading' },
    { key: 'h3', label: 'Subheading' },
    { key: 'h4', label: 'Small heading' },
  ] as const;

  /** The Tiptap editor, once it exists (after the first render). */
  private editor: Editor | null = null;
  /** The HTML the editor holds, so a value written back by this component is not loaded again. */
  private held: string | null = null;

  protected readonly active = signal<Active>(NOTHING_ACTIVE);
  protected readonly panel = signal<'link' | null>(null);
  protected readonly linkHref = signal('');
  protected readonly linkError = signal<string | null>(null);
  protected readonly picking = signal(false);
  protected readonly pendingImage = signal<PendingImage | null>(null);
  protected readonly altText = signal('');
  protected readonly altError = signal<string | null>(null);

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
      key === 'paragraph'
        ? chain.setParagraph()
        : chain.toggleHeading({ level: key === 'h2' ? 2 : key === 'h3' ? 3 : 4 }),
    );
  }

  protected openLink(): void {
    if (!this.editor) return;
    this.pendingImage.set(null);
    this.linkHref.set((this.editor.getAttributes('link')['href'] as string | undefined) ?? '');
    this.linkError.set(null);
    this.panel.set('link');
    afterNextRender(() => this.linkInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected applyLink(): void {
    const href = this.linkHref().trim();
    if (!isAllowedLink(href)) {
      this.linkError.set(
        href === ''
          ? 'Enter where the link goes.'
          : 'Use a page on this site (/visit), a place on this page (#hours), or a full web, email or phone address.',
      );
      return;
    }
    this.run((chain) => chain.extendMarkRange('link').setLink({ href }));
    this.closePanel();
  }

  protected removeLink(): void {
    this.run((chain) => chain.extendMarkRange('link').unsetLink());
    this.closePanel();
  }

  protected picked(asset: MediaAssetSummary): void {
    this.panel.set(null);
    this.pendingImage.set({ assetId: asset.id, thumbnailUrl: asset.thumbnailUrl });
    this.altText.set(asset.altText ?? '');
    this.altError.set(null);
    afterNextRender(() => this.altInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected insertImage(): void {
    const pending = this.pendingImage();
    const alt = this.altText().trim();
    if (!pending) return;
    if (alt === '') {
      this.altError.set('Describe the picture before it goes in.');
      return;
    }
    this.run((chain) => chain.setImage({ src: mediaImageSrc(pending.assetId), alt }));
    this.closePanel();
  }

  protected closePanel(): void {
    this.panel.set(null);
    this.pendingImage.set(null);
    this.editor?.commands.focus();
  }

  private create(): void {
    const value = this.value();
    this.held = value;
    this.editor = new Editor({
      element: this.surface().nativeElement,
      editable: !this.disabled(),
      content: value,
      extensions: [
        StarterKit.configure({
          heading: { levels: [2, 3, 4] },
          code: false,
          codeBlock: false,
          horizontalRule: false,
          strike: false,
          underline: false,
          link: {
            openOnClick: false,
            autolink: false,
            linkOnPaste: true,
            HTMLAttributes: { target: null, rel: null },
            isAllowedUri: (url) => isAllowedLink(url),
          },
        }),
        // Only library pictures parse: one pasted from elsewhere is dropped, as the server would drop it.
        Image.extend({
          parseHTML: () => [
            {
              tag: 'img[src]',
              getAttrs: (element) =>
                isMediaImageSrc(element.getAttribute('src') ?? '') ? null : false,
            },
          ],
        }).configure({
          inline: false,
          allowBase64: false,
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
    });
    this.refresh();
  }

  private refresh(): void {
    const editor = this.editor;
    if (!editor) return;
    this.active.set({
      paragraph: editor.isActive('paragraph') && !editor.isActive('blockquote'),
      h2: editor.isActive('heading', { level: 2 }),
      h3: editor.isActive('heading', { level: 3 }),
      h4: editor.isActive('heading', { level: 4 }),
      bold: editor.isActive('bold'),
      italic: editor.isActive('italic'),
      bulletList: editor.isActive('bulletList'),
      orderedList: editor.isActive('orderedList'),
      blockquote: editor.isActive('blockquote'),
      link: editor.isActive('link'),
      canUndo: editor.can().undo(),
      canRedo: editor.can().redo(),
    });
  }
}

function setOrRemove(element: Element, name: string, value: string | null): void {
  if (value === null) element.removeAttribute(name);
  else element.setAttribute(name, value);
}
