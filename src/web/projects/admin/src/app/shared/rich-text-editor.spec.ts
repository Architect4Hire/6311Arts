import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Editor } from '@tiptap/core';
import { RichTextEditor } from './rich-text-editor';

const ASSET_ID = '0199f0a0-0000-7000-8000-0000000000aa';

// ProseMirror measures the selection to scroll it into view; jsdom has no layout, so ranges answer with nothing.
Range.prototype.getClientRects ??= () =>
  ({
    length: 0,
    item: () => null,
    [Symbol.iterator]: [][Symbol.iterator],
  }) as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();
document.elementFromPoint ??= () => null;

@Component({
  imports: [RichTextEditor],
  template: `<cms-rich-text label="Text" hint="A hint." [(value)]="value" [error]="error()" />`,
})
class Host {
  readonly value = signal('<h2>Your privacy</h2><ul><li>Name</li></ul>');
  readonly error = signal<string | null>(null);
}

async function render() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const component = fixture.debugElement.children[0].componentInstance as RichTextEditor;
  const editor = (component as unknown as { editor: Editor }).editor;
  return { fixture, root, component, editor, host: fixture.componentInstance };
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

function button(root: HTMLElement, label: string): HTMLButtonElement {
  return [...root.querySelectorAll('button')].find((candidate) => text(candidate) === label)!;
}

describe('RichTextEditor', () => {
  it('shows the body as a labelled, described text box without calling it edited', async () => {
    const { root, host } = await render();

    const textbox = root.querySelector('[role="textbox"]')!;
    expect(textbox.getAttribute('aria-multiline')).toBe('true');
    expect(text(root.querySelector(`#${textbox.getAttribute('aria-labelledby')}`))).toBe('Text');
    expect(text(root.querySelector(`#${textbox.getAttribute('aria-describedby')}`))).toBe(
      'A hint.',
    );
    expect(textbox.querySelector('h2')?.textContent).toBe('Your privacy');
    expect(textbox.querySelector('li')?.textContent).toBe('Name');
    // A labelled group, not role="toolbar": that role promises arrow-key navigation over one tab stop, and
    // every button here is its own tab stop.
    const formatting = root.querySelector('[role="group"]');
    expect(formatting?.getAttribute('aria-label')).toBe('Text formatting');
    expect(root.querySelector('[role="toolbar"]')).toBeNull();

    // Loading normalises the HTML inside the editor, but nothing was edited, so nothing is written back.
    expect(host.value()).toBe('<h2>Your privacy</h2><ul><li>Name</li></ul>');
  });

  it('writes every edit back as HTML within the allow-list', async () => {
    const { fixture, root, editor, host } = await render();

    editor.commands.setTextSelection({ from: 1, to: 5 });
    button(root, 'Bold').click();
    fixture.detectChanges();

    expect(host.value()).toContain('<strong>Your</strong>');
    expect(button(root, 'Bold').getAttribute('aria-pressed')).toBe('true');
  });

  it('marks the text invalid and reads out the error with it', async () => {
    const { fixture, root, host } = await render();

    host.error.set('Write the page first.');
    fixture.detectChanges();

    const textbox = root.querySelector('[role="textbox"]')!;
    expect(textbox.getAttribute('aria-invalid')).toBe('true');
    expect(textbox.getAttribute('aria-describedby')).toContain('-error');
    expect(text(root)).toContain('Write the page first.');
    expect(root.querySelector('.editor')?.classList).toContain('editor--invalid');
  });

  it('shows focus once, on the frame around the toolbar and the text', async () => {
    const { fixture, root, editor } = await render();
    const frame = root.querySelector('.editor')!;

    // The toolbar and the text share the frame; the text draws no box of its own inside it.
    expect(frame.querySelector('[role="group"]')).not.toBeNull();
    expect(frame.querySelector('[role="textbox"]')).not.toBeNull();
    expect(frame.querySelector('.field__control')).toBeNull();

    editor.view.dom.dispatchEvent(new FocusEvent('focus'));
    fixture.detectChanges();
    expect(frame.classList).toContain('editor--focused');

    editor.view.dom.dispatchEvent(new FocusEvent('blur'));
    fixture.detectChanges();
    expect(frame.classList).not.toContain('editor--focused');
  });

  it('offers one heading level and no link, quote or picture (ADR 0045)', async () => {
    const { fixture, root, editor, host } = await render();

    const labels = [...root.querySelectorAll('button')].map((candidate) => text(candidate));
    expect(labels).toEqual([
      'Text',
      'Heading',
      'Bold',
      'Italic',
      'Bullets',
      'Numbers',
      'Undo',
      'Redo',
    ]);

    editor.commands.setTextSelection(3);
    button(root, 'Text').click();
    fixture.detectChanges();
    // Tiptap keeps an empty paragraph after a closing list to type into; the server drops it.
    expect(host.value()).toMatch(/^<p>Your privacy<\/p><ul>/);
    expect(button(root, 'Heading').getAttribute('aria-pressed')).toBe('false');

    button(root, 'Heading').click();
    fixture.detectChanges();
    expect(host.value()).toMatch(/^<h2>Your privacy<\/h2><ul>/);
    expect(button(root, 'Heading').getAttribute('aria-pressed')).toBe('true');
  });

  it('keeps only the words of a link and drops every picture when a body arrives with them', async () => {
    const { fixture, editor, host } = await render();

    host.value.set(
      `<p>See <a href="/visit">Visit</a>.</p><blockquote><p>Quoted</p></blockquote>` +
        `<img src="https://elsewhere.example/a.jpg" alt="x"><img src="/media/${ASSET_ID}/800.webp" alt="ok">`,
    );
    fixture.detectChanges();

    const html = editor.getHTML();
    expect(html).toContain('<p>See Visit.</p>');
    expect(html).toContain('<p>Quoted</p>');
    expect(html).not.toContain('<a');
    expect(html).not.toContain('<blockquote');
    expect(html).not.toContain('<img');
  });
});
