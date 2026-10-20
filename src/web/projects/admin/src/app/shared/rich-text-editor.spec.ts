import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Editor } from '@tiptap/core';
import { MediaAssetSummary } from '../models/media';
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
    expect(root.querySelector('[role="toolbar"]')?.getAttribute('aria-label')).toBe(
      'Text formatting',
    );

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
  });

  it('takes only links the server keeps', async () => {
    const { fixture, root, editor, host } = await render();
    editor.commands.setTextSelection({ from: 1, to: 5 });

    button(root, 'Link').click();
    fixture.detectChanges();
    const href = root.querySelector('[name="linkHref"]') as HTMLInputElement;
    href.value = 'javascript:alert(1)';
    href.dispatchEvent(new Event('input'));
    button(root, 'Apply link').click();
    fixture.detectChanges();
    expect(text(root)).toContain('Use a page on this site');
    expect(host.value()).not.toContain('<a');

    href.value = '/visit';
    href.dispatchEvent(new Event('input'));
    button(root, 'Apply link').click();
    fixture.detectChanges();

    expect(host.value()).toContain('<a href="/visit">Your</a>');
    expect(host.value()).not.toContain('target=');
    expect(root.querySelector('[name="linkHref"]')).toBeNull();
  });

  it('puts a library picture in only with alt text, at its rendition address', async () => {
    const { fixture, root, component, host } = await render();
    const asset = {
      id: ASSET_ID,
      thumbnailUrl: null,
      altText: null,
    } as unknown as MediaAssetSummary;

    (component as unknown as { picked(asset: MediaAssetSummary): void }).picked(asset);
    fixture.detectChanges();
    button(root, 'Insert picture').click();
    fixture.detectChanges();
    expect(text(root)).toContain('Describe the picture before it goes in.');

    const alt = root.querySelector('[name="imageAlt"]') as HTMLInputElement;
    alt.value = 'The gallery’s front window';
    alt.dispatchEvent(new Event('input'));
    button(root, 'Insert picture').click();
    fixture.detectChanges();

    expect(host.value()).toContain(
      `<img src="/media/${ASSET_ID}/800.webp" alt="The gallery’s front window">`,
    );
  });

  it('drops a picture from outside the library when a body arrives with one', async () => {
    const { fixture, editor, host } = await render();

    host.value.set(
      `<p>Look</p><img src="https://elsewhere.example/a.jpg" alt="x"><img src="/media/${ASSET_ID}/800.webp" alt="ok">`,
    );
    fixture.detectChanges();

    expect(editor.getHTML()).not.toContain('elsewhere.example');
    expect(editor.getHTML()).toContain(`/media/${ASSET_ID}/800.webp`);
  });
});
